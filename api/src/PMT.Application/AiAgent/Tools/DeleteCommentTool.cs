using System.Text.Json;
using PMT.Application.Comments;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.UserStories;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes a comment the caller can reach.</summary>
/// <remarks>
/// <para>Destructive, so the orchestrator pauses the turn and asks the user to approve the removal
/// before this tool is ever executed.</para>
/// <para>dbo.Comment stores its parent as one of four nullable foreign keys rather than an
/// EntityType/EntityId pair (see <see cref="AddCommentTool"/>), so the owning project is resolved
/// through whichever key is set. That project is what the conversation scope is checked against;
/// a comment whose parent no longer exists is treated as out of scope rather than deleted blindly.</para>
/// </remarks>
public sealed class DeleteCommentTool(
    ICommentRepository repository,
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues) : IAgentTool
{
    public string Name => "delete_comment";

    public string Description =>
        "Delete a comment from a project, story, task or issue. Resolve the exact commentId first. "
        + "The user is asked to confirm before the deletion actually happens.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "commentId": { "type": "integer", "description": "Id of the comment to delete." }
          },
          "required": ["commentId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.CommentsManage;

    public bool IsDestructive => true;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var commentId = ToolArguments.GetLong(arguments, "commentId");
        if (commentId is null or <= 0)
            return AgentToolResult.Fail("'commentId' is required and must be a positive comment id.");

        var comment = await repository.GetByIdAsync(commentId.Value, cancellationToken);
        if (comment is null || comment.IsDeleted)
            return AgentToolResult.Fail($"Comment {commentId} was not found.");

        if (await ResolveScopeAsync(context, comment.ProjectId, comment.UserStoryId, comment.TaskId, comment.IssueId, cancellationToken)
            is { } scopeError)
            return AgentToolResult.Fail($"Comment {commentId} {scopeError}");

        if (!await repository.DeleteAsync(comment.Id, context.UserId, cancellationToken))
            return AgentToolResult.Fail($"Comment {commentId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = comment.Id
        });
    }

    /// <summary>
    /// Returns null when the comment's parent lies inside the conversation scope, otherwise the
    /// tail of a message explaining why it does not.
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

        // Shared with AgentActionSummarizer so the confirmation sentence and this check always
        // agree on which project owns the comment.
        var owningProjectId = await AgentToolScope.ResolveCommentProjectAsync(
            stories, tasks, issues, projectId, storyId, taskId, issueId, cancellationToken);

        if (owningProjectId is null)
            return "is not attached to a record in this project and is out of scope for this conversation.";

        return AgentToolScope.IsInScope(context, owningProjectId.Value)
            ? null
            : "belongs to another project and is out of scope for this conversation.";
    }
}
