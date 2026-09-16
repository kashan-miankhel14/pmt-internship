using System.Text.Json;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.UserStories;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Fetches one record of any of the four core types, with every field the agent might need,
/// so the model does not have to guess details it only saw summarised in a search result.
/// </summary>
/// <remarks>
/// <see cref="RequiredPermission"/> is null because this tool spans four entity types and the
/// orchestrator can only check a single claim. The per-type view permission is therefore
/// enforced here, in <see cref="Authorize"/>, against the caller's own claims. Null on this
/// tool means "cannot be decided up front", not "open to everyone".
/// </remarks>
public sealed class GetEntityTool(
    IUserStoryRepository stories,
    ITaskRepository tasks,
    IIssueRepository issues,
    AgentToolScope scope,
    ICurrentUserService currentUser) : IAgentTool
{
    public string Name => "get_entity";

    public string Description =>
        "Fetch the full details of a single Project, UserStory, Task or Issue by its id. "
        + "Use this after a search when you need fields the search did not return.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "entityType": {
              "type": "string",
              "enum": ["Project", "UserStory", "Task", "Issue"],
              "description": "The kind of record to fetch."
            },
            "entityId": { "type": "integer", "description": "The id of the record." }
          },
          "required": ["entityType", "entityId"]
        }
        """;

    public string? RequiredPermission => null; // Decided per entity type in Authorize.

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

        if (Authorize(entityType) is { } denied)
            return AgentToolResult.Fail(denied);

        return entityType switch
        {
            "Project" => await GetProjectAsync(context, entityId.Value, cancellationToken),
            "UserStory" => await GetStoryAsync(context, entityId.Value, cancellationToken),
            "Task" => await GetTaskAsync(context, entityId.Value, cancellationToken),
            _ => await GetIssueAsync(context, entityId.Value, cancellationToken)
        };
    }

    private string? Authorize(string entityType)
    {
        var permission = entityType switch
        {
            "Project" => PermissionRequirement.ProjectsView,
            "UserStory" => PermissionRequirement.StoriesView,
            "Task" => PermissionRequirement.TasksView,
            _ => PermissionRequirement.IssuesView
        };

        return currentUser.HasPermission(permission)
            ? null
            : $"You do not have the '{permission}' permission required to read a {entityType}.";
    }

    private async Task<AgentToolResult> GetProjectAsync(AgentToolContext context, long id, CancellationToken cancellationToken)
    {
        var (project, error) = await scope.ResolveProjectAsync(context, id, cancellationToken);
        if (project is null) return AgentToolResult.Fail(error!);

        return AgentToolResult.Ok(new
        {
            entityType = "Project",
            id = project.Id,
            key = project.Key,
            name = project.Name,
            description = project.Description,
            status = project.Status.ToString(),
            ownerUserId = project.OwnerUserId,
            departmentId = project.DepartmentId,
            startDate = project.StartDate,
            targetDate = project.TargetDate,
            active = project.Active
        });
    }

    private async Task<AgentToolResult> GetStoryAsync(AgentToolContext context, long id, CancellationToken cancellationToken)
    {
        var story = await stories.GetByIdAsync(id, cancellationToken);
        if (story is null || story.IsDeleted)
            return AgentToolResult.Fail($"UserStory {id} was not found.");

        if (!AgentToolScope.IsInScope(context, story.ProjectId))
            return AgentToolResult.Fail($"UserStory {id} belongs to another project and is out of scope for this conversation.");

        return AgentToolResult.Ok(new
        {
            entityType = "UserStory",
            id = story.Id,
            projectId = story.ProjectId,
            title = story.Title,
            description = story.Description,
            acceptanceCriteria = story.AcceptanceCriteria,
            status = story.Status.ToString(),
            priority = story.Priority,
            storyPoints = story.StoryPoints,
            assignedToUserId = story.AssigneeUserId,
            active = story.Active
        });
    }

    private async Task<AgentToolResult> GetTaskAsync(AgentToolContext context, long id, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(id, cancellationToken);
        if (task is null || task.IsDeleted)
            return AgentToolResult.Fail($"Task {id} was not found.");

        if (!AgentToolScope.IsInScope(context, task.ProjectId))
            return AgentToolResult.Fail($"Task {id} belongs to another project and is out of scope for this conversation.");

        return AgentToolResult.Ok(new
        {
            entityType = "Task",
            id = task.Id,
            projectId = task.ProjectId,
            storyId = task.StoryId,
            title = task.Title,
            description = task.Description,
            status = task.Status.ToString(),
            priority = task.Priority,
            assignedToUserId = task.AssigneeUserId,
            estimatedHours = task.EstimateHours,
            actualHours = task.ActualHours,
            dueDate = task.DueDate,
            completedDate = task.CompletedDate,
            active = task.Active
        });
    }

    private async Task<AgentToolResult> GetIssueAsync(AgentToolContext context, long id, CancellationToken cancellationToken)
    {
        var issue = await issues.GetByIdAsync(id, cancellationToken);
        if (issue is null || issue.IsDeleted)
            return AgentToolResult.Fail($"Issue {id} was not found.");

        if (!AgentToolScope.IsInScope(context, issue.ProjectId))
            return AgentToolResult.Fail($"Issue {id} belongs to another project and is out of scope for this conversation.");

        return AgentToolResult.Ok(new
        {
            entityType = "Issue",
            id = issue.Id,
            projectId = issue.ProjectId,
            taskId = issue.TaskId,
            title = issue.Title,
            description = issue.Description,
            severity = issue.Severity.ToString(),
            status = issue.Status.ToString(),
            reportedByUserId = issue.ReportedByUserId,
            assignedToUserId = issue.AssignedToUserId,
            resolvedDate = issue.ResolvedDate,
            active = issue.Active
        });
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
