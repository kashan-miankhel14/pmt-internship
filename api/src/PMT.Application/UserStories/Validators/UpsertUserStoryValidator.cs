using FluentValidation;
using PMT.Application.Sprints;
using PMT.Application.UserStories.Dtos;

namespace PMT.Application.UserStories.Validators;

/// <summary>
/// Validates a story create/update payload.
/// </summary>
/// <remarks>
/// The sprint repository is injected: validators are registered by assembly scan in
/// <see cref="DependencyInjection.AddApplication"/>, which resolves them from the container with a
/// scoped lifetime, so a repository dependency is wired exactly like a service dependency. The
/// sprint rule is therefore asynchronous, and this validator must be invoked through
/// <c>ValidateAsync</c> — which is what <see cref="UserStoryService"/> already does.
/// </remarks>
public sealed class UpsertUserStoryValidator : AbstractValidator<UpsertUserStoryRequest>
{
    public UpsertUserStoryValidator(ISprintRepository sprints)
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Priority).InclusiveBetween(1, 5);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.StoryPoints).GreaterThanOrEqualTo(0).When(x => x.StoryPoints.HasValue);

        // Null means the story sits in the backlog.
        RuleFor(x => x.SprintId).GreaterThan(0).When(x => x.SprintId.HasValue);

        // Sprint membership is an un-enforced association at the data layer (no foreign key; see
        // 0017_SprintsAndBoards.sql), so the one invariant that matters — a story cannot be
        // committed to another project's sprint — has to be checked here or nowhere. A sprint that
        // cannot be read is deliberately allowed through: SP_SPRINT FETCH filters IsDeleted = 0, so
        // failing on a null would make every story that still points at a deleted sprint
        // uneditable.
        RuleFor(x => x.SprintId)
            .MustAsync(async (request, sprintId, cancellationToken) =>
            {
                var sprint = await sprints.GetByIdAsync(sprintId!.Value, cancellationToken);
                return sprint is null || sprint.ProjectId == request.ProjectId;
            })
            .WithMessage("Sprint does not belong to the story's project.")
            .When(x => x.SprintId is > 0);
    }
}
