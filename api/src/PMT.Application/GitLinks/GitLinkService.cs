using PMT.Application.Common.Interfaces;
using PMT.Application.GitLinks.Dtos;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Application.GitLinks;

public sealed class GitLinkService(IGitLinkRepository repository, ICurrentUserService currentUser)
{
    public async Task<IReadOnlyCollection<GitLinkDto>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
        => (await repository.GetForEntityAsync(entityType, entityId, cancellationToken)).Select(Map).ToArray();

    public Task<long> CreateAsync(CreateGitLinkRequest request, CancellationToken cancellationToken = default)
        => repository.CreateAsync(new GitLink
        {
            EntityType = request.EntityType.Trim(),
            EntityId = request.EntityId,
            ProjectId = request.EntityType.Equals("Project", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            TaskId = request.EntityType.Equals("Task", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            IssueId = request.EntityType.Equals("Issue", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            Provider = request.Provider,
            RepositoryUrl = request.RepositoryUrl.Trim(),
            ReferenceType = request.ReferenceType.Trim(),
            ReferenceId = request.ReferenceId.Trim(),
            ReferenceUrl = request.ReferenceUrl?.Trim(),
            InsertedBy = currentUser.UserId
        }, cancellationToken);

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(GitLink), id);
    }

    private static GitLinkDto Map(GitLink x)
    {
        var entityType = x.ProjectId.HasValue ? "Project" : x.TaskId.HasValue ? "Task" : "Issue";
        var entityId = x.ProjectId ?? x.TaskId ?? x.IssueId ?? 0;
        return new GitLinkDto(x.Id, entityType, entityId, x.Provider, x.RepositoryUrl, x.ReferenceType, x.CommitSha ?? string.Empty, x.PullRequestUrl);
    }
}
