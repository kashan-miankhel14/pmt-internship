using PMT.Application.Comments.Dtos;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Application.Comments;

public sealed class CommentService(ICommentRepository repository, ICurrentUserService currentUser)
{
    public async Task<IReadOnlyCollection<CommentDto>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
        => (await repository.GetForEntityAsync(entityType, entityId, cancellationToken)).Select(Map).ToArray();

    public Task<long> CreateAsync(CreateCommentRequest request, CancellationToken cancellationToken = default)
    {
        var entityType = request.EntityType.Trim();
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new ValidationException(new[] { "Comment body is required." });
        if (!IsSupportedEntityType(entityType))
            throw new ValidationException(new[] { "Comments can only be attached to projects, user stories, tasks or issues." });

        return repository.CreateAsync(new Comment
        {
            EntityType = entityType,
            EntityId = request.EntityId,
            ProjectId = entityType.Equals("Project", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            UserStoryId = entityType.Equals("UserStory", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            TaskId = entityType.Equals("Task", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            IssueId = entityType.Equals("Issue", StringComparison.OrdinalIgnoreCase) ? request.EntityId : null,
            UserId = currentUser.UserId ?? throw new UnauthorizedAccessException(),
            Content = body,
            InsertedBy = currentUser.UserId
        }, cancellationToken);
    }

    public async Task UpdateAsync(UpdateCommentRequest request, CancellationToken cancellationToken = default)
    {
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new ValidationException(new[] { "Comment body is required." });

        var comment = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Comment), request.Id);

        if (comment.UserId != currentUser.UserId)
            throw new UnauthorizedAccessException("Only the comment author can edit the comment.");

        comment.Content = body;
        comment.UpdatedBy = currentUser.UserId;
        comment.UpdateDate = DateTime.UtcNow;

        if (!await repository.UpdateAsync(comment, cancellationToken))
            throw new NotFoundException(nameof(Comment), request.Id);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(Comment), id);
    }

    private static bool IsSupportedEntityType(string entityType) =>
        entityType.Equals("UserStory", StringComparison.OrdinalIgnoreCase) ||
        entityType.Equals("Project", StringComparison.OrdinalIgnoreCase) ||
        entityType.Equals("Task", StringComparison.OrdinalIgnoreCase) ||
        entityType.Equals("Issue", StringComparison.OrdinalIgnoreCase);

    private static CommentDto Map(Comment x)
    {
        var entityType = x.ProjectId.HasValue ? "Project" : x.UserStoryId.HasValue ? "UserStory" : x.TaskId.HasValue ? "Task" : "Issue";
        var entityId = x.ProjectId ?? x.UserStoryId ?? x.TaskId ?? x.IssueId ?? 0;
        return new CommentDto(x.Id, entityType, entityId, x.UserId, x.Content, x.InsertDate);
    }
}
