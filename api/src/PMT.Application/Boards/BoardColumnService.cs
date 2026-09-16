using FluentValidation;
using PMT.Application.Boards.Dtos;
using PMT.Application.Common.Interfaces;
using PMT.Application.Projects;
using PMT.Domain.Common;
using PMT.Domain.Entities;

namespace PMT.Application.Boards;

/// <summary>
/// Application service for per-project board column configuration. Columns are always
/// addressed within a project, so callers resolve the route's project key through
/// <see cref="ResolveProjectIdAsync"/> first and pass the surrogate id in.
/// </summary>
/// <remarks>
/// <para>
/// Writes re-read the project's board first. That serves two purposes: it confirms the
/// column being changed actually belongs to the project in the route (the id alone is
/// enough to address any row), and it turns a duplicate name into a plain 400 instead of
/// letting UQ_boardcolumns_project_name surface as an unhandled SQL error.
/// </para>
/// <para>
/// A board is a handful of rows read once per write, so the extra round trip is not worth
/// optimising away.
/// </para>
/// </remarks>
public sealed class BoardColumnService(
    IBoardColumnRepository repository,
    IProjectAccessRepository projectAccessRepository,
    IValidator<UpsertBoardColumnRequest> validator,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// Resolves the project key carried in the route to its surrogate id, or <c>null</c>
    /// when the key matches no live project. Callers surface that as a 404.
    /// </summary>
    public Task<long?> ResolveProjectIdAsync(string projectKey)
        => projectAccessRepository.GetProjectIdByKeyAsync(projectKey);

    public async Task<IReadOnlyCollection<BoardColumnDto>> GetByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        return columns.Select(Map).ToArray();
    }

    public async Task<Result<long>> CreateAsync(long projectId, UpsertBoardColumnRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result<long>.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        var name = request.Name.Trim();
        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        if (columns.Any(column => NameMatches(column, name)))
            return Result<long>.Failure($"A board column named '{name}' already exists on this project.");

        var entity = new BoardColumn
        {
            ProjectId = projectId,
            CreatedBy = currentUser.UserId,
            InsertedBy = currentUser.UserId
        };
        Apply(entity, request);
        return Result<long>.Success(await repository.CreateAsync(entity, cancellationToken));
    }

    public async Task<Result> UpdateAsync(long projectId, long id, UpsertBoardColumnRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return Result.Failure(validation.Errors.Select(x => x.ErrorMessage).ToArray());

        var name = request.Name.Trim();
        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        if (columns.All(column => column.Id != id))
            return Result.Failure(NotFound(id));

        // The column keeping its own name is not a clash.
        if (columns.Any(column => column.Id != id && NameMatches(column, name)))
            return Result.Failure($"A board column named '{name}' already exists on this project.");

        var entity = new BoardColumn
        {
            Id = id,
            ProjectId = projectId,
            UpdateDate = DateTime.UtcNow,
            UpdatedBy = currentUser.UserId
        };
        Apply(entity, request);

        return await repository.UpdateAsync(entity, cancellationToken)
            ? Result.Success()
            : Result.Failure(NotFound(id));
    }

    public async Task<Result> DeleteAsync(long projectId, long id, CancellationToken cancellationToken = default)
    {
        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        if (columns.All(column => column.Id != id))
            return Result.Failure(NotFound(id));

        return await repository.DeleteAsync(id, currentUser.UserId, cancellationToken)
            ? Result.Success()
            : Result.Failure(NotFound(id));
    }

    private static bool NameMatches(BoardColumn column, string name)
        => string.Equals(column.Name, name, StringComparison.OrdinalIgnoreCase);

    private static string NotFound(long id) => $"Board column with id '{id}' was not found.";

    private static void Apply(BoardColumn entity, UpsertBoardColumnRequest request)
    {
        entity.Name = request.Name.Trim();
        entity.Ordinal = request.Ordinal;
        entity.CompleteColumn = request.CompleteColumn;
    }

    private static BoardColumnDto Map(BoardColumn x)
        => new(x.Id, x.ProjectId, x.Name, x.Ordinal, x.CompleteColumn, x.Active);
}
