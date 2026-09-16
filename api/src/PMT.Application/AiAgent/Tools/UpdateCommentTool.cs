using System.Text.Json;
using PMT.Application.Comments;
using PMT.Application.Comments.Dtos;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.UserStories;
using PMT.Domain.Exceptions;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Rewrites the text of a comment the calling user wrote.</summary>
/// <remarks>
/// <para>This is the one tool that calls an application service rather than a repository, and
/// deliberately: <see cref="CommentService.UpdateAsync"/> is where the author-only rule lives
/// (<c>comment.UserId != currentUser.UserId</c> throws), and going straight to
/// <see cref="ICommentRepository.UpdateAsync"/> would hand the agent an edit path the REST
/// surface does not have — Anna could rewrite anyone's words. So the service is reused as-is and
/// its refusal is translated into a sentence the model can relay, never swallowed or worked
/// around.</para>
/// <para>The comment is loaded first anyway, for two reasons the service cannot cover: the
/// conversation's project pin is checked against the comment's parent exactly as
/// <see cref="DeleteCommentTool"/> checks it, and the author id is known before the call so a
/// refusal can say who actually owns the comment instead of only that the edit was denied.</para>
/// <para>Not destructive: the previous text is replaced rather than a record removed, which is
/// the same judgement <c>update_*</c> makes elsewhere. Anna is still expected to read the new
/// wording back and get a yes before calling it.</para>
/// </remarks>
public sealed class UpdateCommentTool(
    CommentService service,
    ICommentRepository repository,
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues) : IAgentTool
{
    /// <summary>Matches the bound AddCommentTool applies to the same column (varchar(max)).</summary>
    private const int MaxTextLength = 10_000;

    public string Name => "update_comment";

    public string Description =>
        "Change the text of an existing comment. Only the person who wrote a comment may edit it. "
        + "Give the commentId — get it from list_comments — and the full replacement text, because "
        + "the new text replaces the old one entirely rather than being appended.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "commentId": { "type": "integer", "description": "Id of the comment to edit. Get it from list_comments." },
            "text": { "type": "string", "description": "The full replacement text of the comment. Max 10000 characters." }
          },
          "required": ["commentId", "text"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.CommentsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var commentId = ToolArguments.GetLong(arguments, "commentId");
        if (commentId is null or <= 0)
            return AgentToolResult.Fail("'commentId' is required and must be a positive comment id. Use list_comments to find it.");

        // 'text' is what the schema advertises; 'body' is accepted too because that is what
        // add_comment calls the same field and a model will carry the habit across.
        var text = ToolArguments.GetString(arguments, "text") ?? ToolArguments.GetString(arguments, "body");
        if (text is null)
            return AgentToolResult.Fail("'text' is required and cannot be blank.");

        if (text.Length > MaxTextLength)
            return AgentToolResult.Fail($"'text' must be {MaxTextLength} characters or fewer.");

        var comment = await repository.GetByIdAsync(commentId.Value, cancellationToken);
        if (comment is null || comment.IsDeleted)
            return AgentToolResult.Fail($"Comment {commentId} was not found.");

        if (await ResolveScopeAsync(context, comment.ProjectId, comment.UserStoryId, comment.TaskId, comment.IssueId, cancellationToken)
            is { } scopeError)
            return AgentToolResult.Fail($"Comment {commentId} {scopeError}");

        // The service enforces this too and is the authority; checking here only buys a message
        // that names the author instead of a bare refusal.
        if (comment.UserId != context.UserId)
            return AgentToolResult.Fail(
                $"Comment {commentId} was written by user {comment.UserId} and only its author can edit it.");

        try
        {
            await service.UpdateAsync(new UpdateCommentRequest(commentId.Value, text), cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return AgentToolResult.Fail($"Only the author of comment {commentId} can edit it. The edit was refused.");
        }
        catch (NotFoundException)
        {
            return AgentToolResult.Fail($"Comment {commentId} was not found.");
        }
        catch (ValidationException ex)
        {
            return AgentToolResult.Fail(string.Join(" ", ex.Errors));
        }

        var updated = await repository.GetByIdAsync(commentId.Value, cancellationToken);

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = commentId.Value,
            userId = comment.UserId,
            body = updated?.Content ?? text,
            updateDate = updated?.UpdateDate
        });
    }

    /// <summary>
    /// Returns null when the comment's parent lies inside the conversation scope, otherwise the
    /// tail of a message explaining why it does not. Same rule, and same wording, as
    /// <see cref="DeleteCommentTool"/>.
    /// </summary>
    private async Task<string?> ResolveScopeAsync(
        AgentToolContext context,
        long? projectId,
        long? storyId,
        long? taskId,
        long? issueId,
        CancellationToken cancellationToken)
    {
        // An unpinned conversation can reach any record the caller's permissions allow, so the
        // parent lookup is skipped entirely.
        if (context.ProjectId is null) return null;

        var owningProjectId = await AgentToolScope.ResolveCommentProjectAsync(
            stories, tasks, issues, projectId, storyId, taskId, issueId, cancellationToken);

        if (owningProjectId is null)
            return "is not attached to a record in this project and is out of scope for this conversation.";

        return AgentToolScope.IsInScope(context, owningProjectId.Value)
            ? null
            : "belongs to another project and is out of scope for this conversation.";
    }
}
