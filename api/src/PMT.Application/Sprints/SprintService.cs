using FluentValidation;
using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Common.Realtime;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;
using PMT.Application.Projects;
using PMT.Application.Sprints.Dtos;
using PMT.Domain.Common;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.Sprints;

/// <summary>
/// Application service for sprint planning. Sprints are always addressed within a
/// project, so callers resolve the route's project key through
/// <see cref="ResolveProjectIdAsync"/> first and pass the surrogate id in.
/// </summary>
/// <remarks>
/// <para>
/// Operations that can fail return a <see cref="Result"/> / <see cref="Result{T}"/>
/// rather than throwing, matching <c>TeamService</c> and <c>ProjectAccessService</c>,
/// the two services that also sit behind project-key routes.
/// </para>
/// <para>
/// Every write takes the resolved project id and checks the sprint actually belongs to
/// it. Without that check a caller holding a valid key for project A could mutate a
/// sprint of project B just by knowing its id, because the sprint id alone is enough to
/// address the row.
/// </para>
/// </remarks>
public sealed class SprintService(
    ISprintRepository repository,
    IProjectAccessRepository projectAccessRepository,
    IProjectRepository projectRepository,
    IValidator<UpsertSprintRequest> validator,
    ICurrentUserService currentUser,
    NotificationService notifications,
    EntityChangeBroadcaster broadcaster,
    ILogger<SprintService> logger)
{
    /// <summary>
    /// Resolves the project key carried in the route to its surrogate id, or <c>null</c>
    /// when the key matches no live project. Callers surface that as a 404.
    /// </summary>
    public Task<long?> ResolveProjectIdAsync(string projectKey)
        => projectAccessRepository.GetProjectIdByKeyAsync(projectKey);

    public async Task<PagedResult<SprintDto>> GetPagedAsync(long projectId, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var result = await repository.GetPagedAsync(projectId, page, pageSize, search, cancellationToken);
        return new PagedResult<SprintDto>(result.Items.Select(Map).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<Result<SprintDto>> GetByIdAsync(long projectId, long id, CancellationToken cancellationToken = default)
    {
        var entity = await FindInProjectAsync(projectId, id, cancellationToken);
        return entity is null
            ? Result<SprintDto>.Failure(NotFound(id))
            : Result<SprintDto>.Success(Map(entity));
    }

    public async Task<Result<long>> CreateAsync(long projectId, UpsertSprintRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result<long>.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        // Active falls back to true on create so a plain create (which now omits Active from
        // the body by default) yields an active sprint, matching SP_SPRINT's ISNULL(@Active,1)
        // default and the previous DTO default. The apply step leaves it untouched when the
        // request did not supply a value.
        var entity = new Sprint { ProjectId = projectId, Status = SprintStatus.Planned, Active = request.Active ?? true, InsertedBy = currentUser.UserId };
        Apply(entity, request);
        var id = await repository.CreateAsync(entity, cancellationToken);
        await broadcaster.PublishAsync(projectId, EntityChangeTypes.Sprint, id, EntityChangeActions.Created, cancellationToken);
        return Result<long>.Success(id);
    }

    public async Task<Result> UpdateAsync(long projectId, long id, UpsertSprintRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        var entity = await FindInProjectAsync(projectId, id, cancellationToken);
        if (entity is null)
            return Result.Failure(NotFound(id));

        Apply(entity, request);
        entity.UpdateDate = DateTime.UtcNow;
        entity.UpdatedBy = currentUser.UserId;

        if (!await repository.UpdateAsync(entity, cancellationToken))
            return Result.Failure(NotFound(id));

        await broadcaster.PublishAsync(projectId, EntityChangeTypes.Sprint, id, EntityChangeActions.Updated, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long projectId, long id, CancellationToken cancellationToken = default)
    {
        var entity = await FindInProjectAsync(projectId, id, cancellationToken);
        if (entity is null)
            return Result.Failure(NotFound(id));

        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            return Result.Failure(NotFound(id));

        await broadcaster.PublishAsync(projectId, EntityChangeTypes.Sprint, id, EntityChangeActions.Deleted, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Starts a planned sprint. The PLANNED-only guard is enforced by the procedure; the
    /// sprint is read first purely so a missing sprint and a wrong-state sprint can be told
    /// apart and reported as 404 and 400 respectively instead of collapsing into one error.
    /// </summary>
    /// <remarks>
    /// The transition itself is a single guarded UPDATE inside <c>dbo.usp_Sprint_Start</c>,
    /// mirroring <see cref="CompleteAsync"/>. Writing it through the ordinary update path
    /// would make the check read-then-write, so a sprint completed between the read and the
    /// write would be silently dragged back to ACTIVE; the procedure matches no row in that
    /// case and the caller is told the status changed underneath them.
    /// </remarks>
    public async Task<Result> StartAsync(long projectId, long id, CancellationToken cancellationToken = default)
    {
        var entity = await FindInProjectAsync(projectId, id, cancellationToken);
        if (entity is null)
            return Result.Failure(NotFound(id));

        if (entity.Status != SprintStatus.Planned)
            return Result.Failure($"Sprint '{entity.Name}' is {entity.Status} and cannot be started. Only a PLANNED sprint can be started.");

        var started = await repository.StartAsync(id, currentUser.UserId, cancellationToken);
        if (started <= 0)
            return Result.Failure($"Sprint '{entity.Name}' could not be started because its status changed concurrently.");

        await broadcaster.PublishAsync(projectId, EntityChangeTypes.Sprint, id, EntityChangeActions.Updated, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Completes an active sprint. The ACTIVE-only guard is enforced by the procedure; the
    /// sprint is read first purely so a missing sprint and a wrong-state sprint can be told
    /// apart and reported as 404 and 400 respectively instead of collapsing into one error.
    /// </summary>
    /// <remarks>
    /// Completion is the one sprint transition somebody has to hear about, so the project owner is
    /// notified. The owner is the meaningful recipient rather than the caller: whoever clicked the
    /// button already knows, and the sprint may well be closed by a scrum master on the owner's
    /// behalf. The project is read once and serves both the notification and the change broadcast.
    /// </remarks>
    public async Task<Result> CompleteAsync(long projectId, long id, CancellationToken cancellationToken = default)
    {
        var entity = await FindInProjectAsync(projectId, id, cancellationToken);
        if (entity is null)
            return Result.Failure(NotFound(id));

        if (entity.Status != SprintStatus.Active)
            return Result.Failure($"Sprint '{entity.Name}' is {entity.Status} and cannot be completed. Only an ACTIVE sprint can be completed.");

        var completed = await repository.CompleteAsync(id, currentUser.UserId, cancellationToken);
        if (completed <= 0)
            return Result.Failure($"Sprint '{entity.Name}' could not be completed because its status changed concurrently.");

        var project = await LoadProjectAsync(projectId, cancellationToken);
        await NotifyProjectOwnerAsync(project, projectId, entity, cancellationToken);
        await broadcaster.PublishForKeyAsync(project?.Key, EntityChangeTypes.Sprint, id, EntityChangeActions.Updated, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Reads the project behind an already-authorised write, or <c>null</c> when it cannot be read.
    /// Only side effects depend on it, so a failure here must not turn a completed sprint into an
    /// error.
    /// </summary>
    private async Task<Project?> LoadProjectAsync(long projectId, CancellationToken cancellationToken)
    {
        try
        {
            return await projectRepository.GetByIdAsync(projectId, cancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not read project {ProjectId} after a sprint transition; the notification and broadcast were skipped.", projectId);

            return null;
        }
    }

    /// <summary>
    /// Tells the project owner their sprint closed. Best effort for the same reason as
    /// <see cref="LoadProjectAsync"/>: the transition is already committed.
    /// </summary>
    private async Task NotifyProjectOwnerAsync(Project? project, long projectId, Sprint sprint, CancellationToken cancellationToken)
    {
        if (project is null || project.OwnerUserId <= 0)
            return;

        try
        {
            await notifications.CreateAsync(new CreateNotificationRequest(
                project.OwnerUserId,
                "sprint.completed",
                "Sprint completed",
                $"Sprint '{sprint.Name}' was completed in {project.Key}.",
                $"/projects/{projectId}"), cancellationToken);
        }
        catch (Exception ex)
        {
            // Deliberately swallowed; the sprint is already completed.
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not notify the owner of project {ProjectId} that sprint {SprintId} was completed.", projectId, sprint.Id);
        }
    }

    /// <summary>
    /// Loads a sprint only when it belongs to the project addressed by the route. A sprint
    /// of another project is reported as missing rather than as forbidden, so the endpoint
    /// does not confirm that the id exists elsewhere.
    /// </summary>
    private async Task<Sprint?> FindInProjectAsync(long projectId, long id, CancellationToken cancellationToken)
    {
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        return entity is not null && entity.ProjectId == projectId ? entity : null;
    }

    private static string NotFound(long id) => $"Sprint with id '{id}' was not found.";

    /// <summary>
    /// Copies the payload onto the entity. <c>Status</c> is optional: a body that omits it
    /// leaves the stored status alone, so an ordinary PUT of name/goal/dates can no longer
    /// reset a running sprint back to PLANNED. A create starts from
    /// <see cref="SprintStatus.Planned"/>, which is what the entity and SP_SPRINT both
    /// default to.
    /// </summary>
    private static void Apply(Sprint entity, UpsertSprintRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Goal = request.Goal?.Trim();
        entity.StartDate = request.StartDate;
        entity.EndDate = request.EndDate;
        if (request.Status.HasValue)
            entity.Status = request.Status.Value;
        if (request.Active.HasValue)
            entity.Active = request.Active.Value;
    }

    private static SprintDto Map(Sprint x)
        => new(x.Id, x.ProjectId, x.Name, x.Goal, x.StartDate, x.EndDate, x.Status, x.Active);
}
