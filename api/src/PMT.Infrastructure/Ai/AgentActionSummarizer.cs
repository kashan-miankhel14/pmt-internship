using System.Text.Json;
using Microsoft.Extensions.Logging;
using PMT.Application.AiAgent.Tools;
using PMT.Application.Comments;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.Teams;
using PMT.Application.UserStories;
using PMT.Domain.Entities;
// Aliased rather than imported: PMT.Application.AiAgent.Tools declares a ToolArguments of its
// own, and naming the infrastructure one outright keeps the reference unambiguous.
using ToolArguments = PMT.Infrastructure.Ai.Tools.ToolArguments;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Turns a pending destructive tool call into the sentence the user is asked to approve,
/// for example <c>Delete story #1289 "Login with Google"</c>.
/// </summary>
/// <remarks>
/// <para>The title is read from the record itself rather than from anything the model said, so the
/// user is shown what will actually be deleted and not what the model believes it is deleting.
/// That is the whole point of the confirmation step, so a mismatch here would defeat it.</para>
/// <para><b>This runs before the delete tool's own scope check</b>, so it repeats that check
/// itself. A summary is written into the confirmation prompt, the persisted assistant message and
/// the failure text, all of which the caller can read; without the check a chat pinned to project
/// A could learn a title or a comment excerpt from project B just by naming an id that the delete
/// would afterwards refuse. The read permission is required for the same reason: asking to delete
/// something is not a licence to read it.</para>
/// <para>Every lookup therefore degrades to the id-only form — <c>Delete story #999</c> — when the
/// record is out of the conversation's project scope, when the caller does not hold the matching
/// view permission, or when it simply cannot be read. Summarising must never break a turn, so an
/// unrecognised tool falls back to its name and any failure falls back with it.</para>
/// </remarks>
public sealed class AgentActionSummarizer(
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues,
    ICommentRepository comments,
    ITeamRepository teams,
    ICurrentUserService currentUser,
    ILogger<AgentActionSummarizer> logger)
{
    /// <summary>Longest comment excerpt shown in a summary before it is elided.</summary>
    private const int MaxExcerptLength = 60;

    /// <summary>
    /// Describes one pending call in plain language, revealing a record's title or excerpt only
    /// when <paramref name="context"/> and the caller's permissions allow reading it.
    /// </summary>
    /// <param name="context">The turn's tool context, whose ProjectId pins the readable scope.</param>
    /// <param name="toolName">Tool the model asked for.</param>
    /// <param name="argumentsJson">Arguments exactly as the model produced them.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    public async Task<string> DescribeAsync(
        AgentToolContext context, string toolName, string? argumentsJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var arguments = Parse(argumentsJson);

        try
        {
            return toolName.ToLowerInvariant() switch
            {
                "delete_story" => await DescribeStoryAsync(context, arguments, cancellationToken),
                "delete_task" => await DescribeTaskAsync(context, arguments, cancellationToken),
                "delete_issue" => await DescribeIssueAsync(context, arguments, cancellationToken),
                "delete_comment" => await DescribeCommentAsync(context, arguments, cancellationToken),
                "delete_team" => await DescribeTeamAsync(arguments),
                "delete_board_column" => DescribeBoardColumnDeletion(arguments),
                "remove_project_member" => DescribeProjectMemberRemoval(arguments),
                "remove_team_member" => DescribeTeamMemberRemoval(arguments),
                "remove_project_team" => DescribeProjectTeamRemoval(arguments),
                _ => Fallback(toolName, arguments)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not describe pending action {ToolName}; falling back to the generic summary.", toolName);
            return Fallback(toolName, arguments);
        }
    }

    private async Task<string> DescribeStoryAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var id = Id(arguments, "storyId");
        if (id is null) return "Delete a story";

        if (!currentUser.HasPermission(PermissionRequirement.StoriesView))
            return Compose("story", id.Value, null);

        var story = await stories.GetByIdAsync(id.Value, cancellationToken);
        var readable = story is { IsDeleted: false } && AgentToolScope.IsInScope(context, story.ProjectId);

        return Compose("story", id.Value, readable ? story!.Title : null);
    }

    private async Task<string> DescribeTaskAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var id = Id(arguments, "taskId");
        if (id is null) return "Delete a task";

        if (!currentUser.HasPermission(PermissionRequirement.TasksView))
            return Compose("task", id.Value, null);

        var task = await tasks.GetByIdAsync(id.Value, cancellationToken);
        var readable = task is { IsDeleted: false } && AgentToolScope.IsInScope(context, task.ProjectId);

        return Compose("task", id.Value, readable ? task!.Title : null);
    }

    private async Task<string> DescribeIssueAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var id = Id(arguments, "issueId");
        if (id is null) return "Delete an issue";

        if (!currentUser.HasPermission(PermissionRequirement.IssuesView))
            return Compose("issue", id.Value, null);

        var issue = await issues.GetByIdAsync(id.Value, cancellationToken);
        var readable = issue is { IsDeleted: false } && AgentToolScope.IsInScope(context, issue.ProjectId);

        return Compose("issue", id.Value, readable ? issue!.Title : null);
    }

    /// <remarks>
    /// There is no <c>comments.view</c> key in <see cref="PermissionRequirement"/> — the 17
    /// canonical permissions are seeded in SQL and comments carry only <c>comments.manage</c> —
    /// so that is the claim gating the excerpt here. It is the same claim the delete tool
    /// requires, which keeps the two consistent without inventing a permission no role can hold.
    /// </remarks>
    private async Task<string> DescribeCommentAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var id = Id(arguments, "commentId");
        if (id is null) return "Delete a comment";

        if (!currentUser.HasPermission(PermissionRequirement.CommentsManage))
            return Compose("comment", id.Value, null);

        var comment = await comments.GetByIdAsync(id.Value, cancellationToken);
        if (comment is null || comment.IsDeleted) return Compose("comment", id.Value, null);

        var readable = await IsCommentInScopeAsync(context, comment, cancellationToken);
        return Compose("comment", id.Value, readable ? Excerpt(comment.Content) : null);
    }

    /// <summary>
    /// Mirrors DeleteCommentTool's containment rule through the shared resolver: an unpinned
    /// conversation reaches anything the caller's permissions allow, and a comment whose parent
    /// cannot be reached is treated as out of scope rather than described.
    /// </summary>
    private async Task<bool> IsCommentInScopeAsync(AgentToolContext context, Comment comment, CancellationToken cancellationToken)
    {
        if (context.ProjectId is null) return true;

        var owningProjectId = await AgentToolScope.ResolveCommentProjectAsync(
            stories, tasks, issues, comment.ProjectId, comment.UserStoryId, comment.TaskId, comment.IssueId, cancellationToken);

        return owningProjectId is { } projectId && AgentToolScope.IsInScope(context, projectId);
    }

    /// <summary>
    /// Teams are not project-scoped, so there is nothing to contain here beyond the read
    /// permission: the REST surface gates team reads on projects.view, and asking to delete one
    /// is not a licence to learn its name.
    /// </summary>
    private async Task<string> DescribeTeamAsync(JsonElement arguments)
    {
        var id = Id(arguments, "teamId");
        if (id is null) return "Delete a team";

        if (!currentUser.HasPermission(PermissionRequirement.ProjectsView))
            return Compose("team", id.Value, null);

        var team = await teams.GetByIdAsync(id.Value);
        return Compose("team", id.Value, team?.Name);
    }

    private static string Compose(string noun, long id, string? label) =>
        string.IsNullOrWhiteSpace(label)
            ? $"Delete {noun} #{id}"
            : $"Delete {noun} #{id} \"{label}\"";

    /// <summary>
    /// delete_board_column: removes one column from a project's board.
    /// </summary>
    /// <remarks>
    /// Arguments-only, like the remove_* summaries below and unlike the record summaries above: the
    /// column's name would have to be read through the whole board, and both ids here came from the
    /// model's own arguments, which the caller is already shown verbatim. The generic fallback is
    /// not enough on its own — the id lives under <c>columnId</c>, so it would render as the
    /// unqualified "Run delete board column".
    /// </remarks>
    private static string DescribeBoardColumnDeletion(JsonElement arguments)
    {
        var column = Reference("board column", Id(arguments, "columnId"));
        var projectKey = ToolArguments.GetString(arguments, "projectKey");

        return string.IsNullOrWhiteSpace(projectKey)
            ? $"Delete {column}"
            : $"Delete {column} from project {projectKey}";
    }

    // ------------------------------------------------------------------
    // Access revocations
    // ------------------------------------------------------------------
    //
    // The three remove_* tools are gated by a confirmation like the deletes, so they need a
    // sentence of their own: the generic fallback ("Run remove project member") names no ids at
    // all, which is not enough for a user to approve a revocation on.
    //
    // Unlike the delete summaries these do no lookups and reveal no titles or names. Every id
    // below came from the model's own arguments, which the caller is already shown verbatim in
    // AgentPendingActionDto.ArgumentsJson, so nothing here can disclose a record the caller
    // could not otherwise see and there is no scope or permission check to repeat.

    /// <summary>remove_project_member: revokes one user's direct membership of a project.</summary>
    private static string DescribeProjectMemberRemoval(JsonElement arguments)
    {
        var user = Reference("user", ToolArguments.GetLong(arguments, "userId"));
        var project = Reference("project", ToolArguments.GetLong(arguments, "projectId"));

        return $"Remove {user} from {project}";
    }

    /// <summary>remove_team_member: revokes one user's membership of a team.</summary>
    private static string DescribeTeamMemberRemoval(JsonElement arguments)
    {
        var user = Reference("user", ToolArguments.GetLong(arguments, "userId"));
        var team = Reference("team", ToolArguments.GetLong(arguments, "teamId"));

        return $"Remove {user} from {team}";
    }

    /// <summary>remove_project_team: revokes a team's whole grant on a project.</summary>
    private static string DescribeProjectTeamRemoval(JsonElement arguments)
    {
        var team = Reference("team", ToolArguments.GetLong(arguments, "teamId"));
        var project = Reference("project", ToolArguments.GetLong(arguments, "projectId"));

        return $"Remove {team}'s access to {project}";
    }

    /// <summary>
    /// "user #7" when the model supplied the id, "a user" when it did not. An argument the model
    /// omitted must not silently become "#0" in a sentence the user is asked to approve.
    /// </summary>
    private static string Reference(string noun, long? id) =>
        id is null ? $"a {noun}" : $"{noun} #{id.Value}";

    /// <summary>Unknown destructive tool: name it plainly rather than pretending to know its shape.</summary>
    private static string Fallback(string toolName, JsonElement arguments)
    {
        var id = Id(arguments, "id");
        var readable = toolName.Replace('_', ' ');
        return id is null ? $"Run {readable}" : $"Run {readable} on #{id}";
    }

    /// <summary>Reads the id under its documented name, tolerating the model's usual "id" shorthand.</summary>
    private static long? Id(JsonElement arguments, string name) =>
        ToolArguments.GetLong(arguments, name) ?? ToolArguments.GetLong(arguments, "id");

    private static string? Excerpt(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var collapsed = string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxExcerptLength ? collapsed : collapsed[..MaxExcerptLength] + "...";
    }

    private static JsonElement Parse(string? argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }
}
