using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Raises an issue against a project, optionally linked to a specific task.</summary>
/// <remarks>
/// ReportedByUserId is taken from <see cref="AgentToolContext.UserId"/> and is never accepted as
/// an argument: the agent acts as the calling user, so it must not be able to file an issue in
/// somebody else's name.
/// </remarks>
public sealed class CreateIssueTool(
    IIssueRepository repository,
    ITaskRepository tasks,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "create_issue";

    public string Description =>
        "Raise a new issue or defect against a project, optionally linked to a task. "
        + "The issue is reported as the current user and starts in the Open status. "
        + "Anna must have every required value from the user before calling this: ask for anything "
        + "missing with ask_for_fields, then read the details back and wait for the user's yes.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer", "description": "Project the issue belongs to." },
            "title": { "type": "string", "description": "Short summary of the issue. Max 250 characters." },
            "description": { "type": "string", "description": "Steps to reproduce, expected and actual behaviour." },
            "severity": {
              "type": "string",
              "enum": ["Low", "Medium", "High", "Critical"],
              "description": "Impact of the issue. Defaults to Medium."
            },
            "taskId": { "type": "integer", "description": "Task this issue was found against, if any." },
            "assignedToUserId": { "type": "integer", "description": "User the issue is assigned to." }
          },
          "required": ["projectId", "title"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.IssuesManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is null)
            return AgentToolResult.Fail("'projectId' is required. Use search_projects to resolve a project name to its id.");

        var (project, projectError) = await scope.ResolveProjectAsync(context, projectId.Value, cancellationToken);
        if (project is null) return AgentToolResult.Fail(projectError!);

        var title = ToolArguments.GetString(arguments, "title");
        if (AgentToolScope.ValidateTitle(title) is { } titleError)
            return AgentToolResult.Fail(titleError);

        if (!ToolArguments.TryGetEnum<IssueSeverity>(arguments, "severity", out var severity, out var severityError))
            return AgentToolResult.Fail(severityError!);

        long? taskId = null;
        if (ToolArguments.Has(arguments, "taskId"))
        {
            taskId = ToolArguments.GetLong(arguments, "taskId");
            if (taskId is null or <= 0)
                return AgentToolResult.Fail("'taskId' must be a positive task id.");

            var task = await tasks.GetByIdAsync(taskId.Value, cancellationToken);
            if (task is null || task.IsDeleted)
                return AgentToolResult.Fail($"Task {taskId} was not found.");

            if (task.ProjectId != project.Id)
                return AgentToolResult.Fail($"Task {taskId} belongs to project {task.ProjectId}, not project {project.Id}.");
        }

        var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
        if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
            return AgentToolResult.Fail(assigneeError);

        var entity = new Issue
        {
            ProjectId = project.Id,
            TaskId = taskId,
            Title = title!,
            Description = ToolArguments.GetString(arguments, "description"),
            Severity = severity ?? IssueSeverity.Medium,
            Status = IssueStatus.Open,
            ReportedByUserId = context.UserId,
            AssignedToUserId = assigneeId,
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The issue could not be created.");

        var created = await repository.GetByIdAsync(id, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            projectId = created?.ProjectId ?? entity.ProjectId,
            taskId = created?.TaskId ?? entity.TaskId,
            title = created?.Title ?? entity.Title,
            description = created?.Description ?? entity.Description,
            severity = (created?.Severity ?? entity.Severity).ToString(),
            status = (created?.Status ?? entity.Status).ToString(),
            reportedByUserId = created?.ReportedByUserId ?? entity.ReportedByUserId,
            assignedToUserId = created?.AssignedToUserId ?? entity.AssignedToUserId
        });
    }
}
