using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Projects;
using PMT.Application.Tasks;
using PMT.Application.Users;
using PMT.Application.UserStories;
using PMT.Application.Workflow;
using PMT.Domain.Exceptions;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the change-log of one story, task or issue, most recent change first.</summary>
/// <remarks>
/// <para>This is how Anna answers "who moved this to Done, and when?". dbo.IssueHistory is a
/// generic log keyed by (EntityType, EntityId) — despite its name it records Stories and Tasks as
/// well as Issues — so the caller names the kind of record as well as its id.</para>
/// <para><b>entityType vocabulary.</b> The model is offered the three words a person would use —
/// <c>story</c>, <c>task</c>, <c>issue</c> — which map onto <see cref="WorkflowEntityKind"/> and
/// are turned into the persisted discriminator by
/// <see cref="WorkflowEntityKindExtensions.ToEntityType"/>. That matters for stories: they are
/// stored as <c>"UserStory"</c>, the only story spelling
/// <c>CK_issuehistory_entitytype</c> accepts, so querying for <c>"Story"</c> would silently return
/// nothing.</para>
/// <para>Mirrors <c>GET /projects/{projectKey}/workflow/history/{entityType}/{entityId}</c>, with
/// one addition the REST route does not need: the log rows carry no project of their own, so the
/// record is loaded first and its project is checked against the key and the conversation's pin.
/// Without that check a history read would be a way around the project scope every other tool
/// honours.</para>
/// </remarks>
public sealed class GetEntityHistoryTool(
    IWorkflowRepository repository,
    IProjectAccessRepository projectAccess,
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues,
    UserService users) : IAgentTool
{
    /// <summary>Newest entries returned in one call, so a long-lived item cannot flood the turn.</summary>
    private const int MaxEntries = 50;

    public string Name => "get_entity_history";

    public string Description =>
        "Read the change log of one story, task or issue, most recent change first: which field "
        + "changed, its old and new value, any comment left with the change, who made it and when. "
        + "Give the project's key (for example 'PMT'), entityType as one of 'story', 'task' or "
        + $"'issue', and the record's entityId. Returns at most the {MaxEntries} newest entries; use "
        + "it to answer questions like 'who moved this to Done and when?'.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project the record belongs to, for example 'PMT'. Resolve it with search_projects." },
            "entityType": {
              "type": "string",
              "enum": ["story", "task", "issue"],
              "description": "Kind of record whose history to read."
            },
            "entityId": { "type": "integer", "description": "Id of the story, task or issue. Get it from search_stories, search_tasks or search_issues." }
          },
          "required": ["projectKey", "entityType", "entityId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var rawType = ToolArguments.GetString(arguments, "entityType");
        if (rawType is null)
            return AgentToolResult.Fail("'entityType' is required and must be one of: story, task, issue.");

        var kind = Normalize(rawType);
        if (kind is null)
            return AgentToolResult.Fail($"Unknown entityType '{rawType}'. Expected one of: story, task, issue.");

        var entityId = ToolArguments.GetLong(arguments, "entityId");
        if (entityId is null or <= 0)
            return AgentToolResult.Fail("'entityId' is required and must be a positive id.");

        // "UserStory" / "Task" / "Issue" — the discriminator the log is actually keyed by.
        var entityType = kind.Value.ToEntityType();

        var entity = await ResolveEntityAsync(kind.Value, entityId.Value, cancellationToken);
        if (entity is null)
            return AgentToolResult.Fail($"{entityType} {entityId} was not found.");

        if (entity.ProjectId != projectId || !AgentToolScope.IsInScope(context, entity.ProjectId))
            return AgentToolResult.Fail(
                $"{entityType} {entityId} belongs to another project and is out of scope for this conversation.");

        var rows = await repository.GetHistoryAsync(entityType, entityId.Value, cancellationToken);

        // SP_ISSUE_HISTORY's FETCH action already returns newest first; the ordering is re-applied
        // so the payload keeps that promise whatever the procedure does later.
        var live = rows
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.ChangedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToArray();

        var page = live.Take(MaxEntries).ToArray();
        var names = await ResolveUserNamesAsync(page.Select(x => x.ChangedByUser), cancellationToken);

        var results = page
            .Select(x => new
            {
                id = x.Id,
                field = x.FieldName,
                oldValue = x.OldValue,
                newValue = x.NewValue,
                comment = x.Comment,
                changedByUserId = x.ChangedByUser,
                changedByUserName = x.ChangedByUser is { } userId && names.TryGetValue(userId, out var name) ? name : null,
                changedAtUtc = x.ChangedAtUtc,
                workflowTransitionId = x.WorkflowTransitionId
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            totalCount = live.Length,
            truncated = live.Length > results.Length,
            projectId,
            entityType,
            entityId = entityId.Value,
            entityTitle = entity.Title,
            history = results
        });
    }

    /// <summary>
    /// Loads the record the log belongs to, or null when it does not exist or is soft-deleted.
    /// The project it returns is what the key and the conversation pin are checked against.
    /// </summary>
    private async Task<EntityRef?> ResolveEntityAsync(
        WorkflowEntityKind kind, long entityId, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case WorkflowEntityKind.Story:
                var story = await stories.GetByIdAsync(entityId, cancellationToken);
                return story is null || story.IsDeleted ? null : new EntityRef(story.ProjectId, story.Title);

            case WorkflowEntityKind.Task:
                var task = await tasks.GetByIdAsync(entityId, cancellationToken);
                return task is null || task.IsDeleted ? null : new EntityRef(task.ProjectId, task.Title);

            default:
                var issue = await issues.GetByIdAsync(entityId, cancellationToken);
                return issue is null || issue.IsDeleted ? null : new EntityRef(issue.ProjectId, issue.Title);
        }
    }

    /// <summary>
    /// Resolves the display names behind the change-log's user ids. "Who moved this?" is the
    /// question this tool exists for, and a bare id is not an answer a user can read. Only the ids
    /// on the returned page are looked up, and each distinct id only once. The lookup goes through
    /// <see cref="UserService"/> rather than <see cref="IUserRepository"/> so repeat readers of the
    /// same log are served from the <c>CacheRegions.Users</c> lookup cache instead of re-querying;
    /// a change log is dominated by a handful of people, so this is usually zero round-trips after
    /// the first turn. An id whose account no longer exists is left out of the map, exactly as the
    /// repository's null result was, and surfaces as a null name.
    /// </summary>
    private async Task<Dictionary<long, string>> ResolveUserNamesAsync(
        IEnumerable<long?> userIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<long, string>();

        foreach (var userId in userIds.OfType<long>().Distinct())
        {
            try
            {
                names[userId] = (await users.GetByIdAsync(userId, cancellationToken)).DisplayName;
            }
            catch (NotFoundException)
            {
                // Deleted account: the change still happened, it just has no name to attach.
            }
        }

        return names;
    }

    /// <summary>Accepts the aliases a model is likely to invent ("user_story", "bug").</summary>
    private static WorkflowEntityKind? Normalize(string value) =>
        value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant() switch
        {
            "story" or "userstory" => WorkflowEntityKind.Story,
            "task" or "taskitem" => WorkflowEntityKind.Task,
            "issue" or "bug" or "defect" => WorkflowEntityKind.Issue,
            _ => null
        };

    /// <summary>The two things this tool needs from the record itself: its project and its title.</summary>
    private sealed record EntityRef(long ProjectId, string Title);
}
