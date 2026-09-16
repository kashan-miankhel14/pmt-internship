using System.Text.Json;
using PMT.Application.Comments;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.Users;
using PMT.Application.UserStories;
using PMT.Domain.Exceptions;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the comment thread hanging off a project, story, task or issue.</summary>
/// <remarks>
/// <para>Mirrors <c>GET /api/v1/comments?entityType=&amp;entityId=</c>: SP_COMMENT's FETCH branch
/// is addressed by an (EntityType, EntityId) pair and turns it into a predicate over whichever of
/// ProjectId / UserStoryId / TaskId / IssueId is set, because dbo.Comment stores its parent as
/// four nullable foreign keys rather than as a type/id pair (see <see cref="AddCommentTool"/>).
/// The four accepted values are therefore exactly 'Project', 'UserStory', 'Task' and 'Issue',
/// spelled the way the procedure compares them; <see cref="Normalize"/> maps the aliases a model
/// is likely to invent ("story", "bug") onto those.</para>
/// <para>The parent is loaded before the thread is read so a comment on another project's task is
/// refused by the conversation's project pin rather than returned, and so a missing parent comes
/// back as "Task 42 was not found" instead of as an empty list the model would read as "no
/// comments yet".</para>
/// <para>Author names are resolved once per distinct commenter rather than per row: the model
/// needs "Priya said" to write a useful answer, and the alternative is a search_users call per
/// comment. The lookup goes through <see cref="UserService"/> rather than
/// <see cref="IUserRepository"/> so it is served from the <c>CacheRegions.Users</c> lookup cache;
/// a busy thread written by the same few people therefore costs one round-trip, not one per
/// commenter per turn.</para>
/// </remarks>
public sealed class ListCommentsTool(
    ICommentRepository repository,
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues,
    UserService users,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "list_comments";

    public string Description =>
        "List the comments on a Project, UserStory, Task or Issue, oldest first, with who wrote "
        + "each one and when. Give the kind of record and its id; resolve the id with "
        + "search_projects, search_stories, search_tasks or search_issues first.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["Project", "UserStory", "Task", "Issue"],
              "description": "The kind of record whose comments should be listed."
            },
            "entityId": { "type": "integer", "description": "Id of the record whose comments should be listed." }
          },
          "required": ["entityType", "entityId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.CommentsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var rawType = ToolArguments.GetString(arguments, "entityType");
        if (rawType is null)
            return AgentToolResult.Fail("'entityType' is required and must be one of: Project, UserStory, Task, Issue.");

        var entityType = Normalize(rawType);
        if (entityType is null)
            return AgentToolResult.Fail($"Unknown entityType '{rawType}'. Expected one of: Project, UserStory, Task, Issue.");

        var entityId = ToolArguments.GetLong(arguments, "entityId");
        if (entityId is null or <= 0)
            return AgentToolResult.Fail("'entityId' is required and must be a positive id.");

        // Resolve the parent first: it proves the record exists and gives the project the
        // conversation's pin is checked against.
        var (projectId, parentError) = await ResolveParentAsync(context, entityType, entityId.Value, cancellationToken);
        if (parentError is not null) return AgentToolResult.Fail(parentError);

        var thread = await repository.GetForEntityAsync(entityType, entityId.Value, cancellationToken);

        // SP_COMMENT already filters IsDeleted and orders by InsertDate; both are re-applied so
        // the payload keeps its shape regardless of what the procedure does later. The pool is
        // bounded for the same reason the searches bound theirs — a busy thread must not fill the
        // model's context window.
        var visible = thread
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.InsertDate)
            .ThenBy(x => x.Id)
            .Take(AgentToolScope.CandidatePoolSize)
            .ToArray();

        var authors = new Dictionary<long, string?>();
        foreach (var userId in visible.Select(x => x.UserId).Distinct())
            authors[userId] = await ResolveDisplayNameAsync(userId, cancellationToken);

        var results = visible
            .Select(x => new
            {
                id = x.Id,
                userId = x.UserId,
                authorName = authors.GetValueOrDefault(x.UserId),
                body = x.Content,
                insertDate = x.InsertDate,
                updateDate = x.UpdateDate
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            incomplete = thread.Count > AgentToolScope.CandidatePoolSize,
            entityType,
            entityId = entityId.Value,
            projectId,
            results
        });
    }

    /// <summary>
    /// Confirms the commented-on record exists and is inside the conversation's scope, and
    /// returns the project that owns it.
    /// </summary>
    private async Task<(long? ProjectId, string? Error)> ResolveParentAsync(
        AgentToolContext context, string entityType, long entityId, CancellationToken cancellationToken)
    {
        switch (entityType)
        {
            case "Project":
            {
                var (project, error) = await scope.ResolveProjectAsync(context, entityId, cancellationToken);
                if (project is null) return (null, error);

                return (project.Id, null);
            }

            case "UserStory":
            {
                var story = await stories.GetByIdAsync(entityId, cancellationToken);
                if (story is null || story.IsDeleted)
                    return (null, $"UserStory {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, story.ProjectId))
                    return (null, $"UserStory {entityId} belongs to another project and is out of scope for this conversation.");

                return (story.ProjectId, null);
            }

            case "Task":
            {
                var task = await tasks.GetByIdAsync(entityId, cancellationToken);
                if (task is null || task.IsDeleted)
                    return (null, $"Task {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, task.ProjectId))
                    return (null, $"Task {entityId} belongs to another project and is out of scope for this conversation.");

                return (task.ProjectId, null);
            }

            default:
            {
                var issue = await issues.GetByIdAsync(entityId, cancellationToken);
                if (issue is null || issue.IsDeleted)
                    return (null, $"Issue {entityId} was not found.");

                if (!AgentToolScope.IsInScope(context, issue.ProjectId))
                    return (null, $"Issue {entityId} belongs to another project and is out of scope for this conversation.");

                return (issue.ProjectId, null);
            }
        }
    }

    /// <summary>
    /// Resolves one commenter's display name through the cached user lookup. A comment can
    /// outlive the account that wrote it, and <see cref="UserService.GetByIdAsync"/> reports a
    /// missing user by throwing, so the miss is turned back into a null name: the row still
    /// belongs in the thread, it simply has no name to attach to it.
    /// </summary>
    private async Task<string?> ResolveDisplayNameAsync(long userId, CancellationToken cancellationToken)
    {
        try
        {
            return (await users.GetByIdAsync(userId, cancellationToken)).DisplayName;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>Accepts the aliases a model is likely to invent ("story", "user_story", "bug").</summary>
    private static string? Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
        {
            "project" => "Project",
            "userstory" or "story" => "UserStory",
            "task" or "taskitem" => "Task",
            "issue" or "bug" or "defect" => "Issue",
            _ => null
        };
}
