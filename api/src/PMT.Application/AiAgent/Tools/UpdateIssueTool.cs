using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Issues;
using PMT.Application.Tasks;
using PMT.Application.Workflow;
using PMT.Application.Workflow.Engine;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Applies a partial update to an issue, including triage changes such as severity and status.
/// </summary>
/// <remarks>
/// Status changes are governed by the data-driven <see cref="WorkflowEngine"/> whenever the
/// project has a workflow scheme (the seeded default does), and fall back to <see cref="IssueStatusRules"/>
/// otherwise. ResolvedDate is maintained the same way <c>IssueService</c> maintains it, so an
/// issue moved by the agent is indistinguishable from one moved in the UI.
/// </remarks>
public sealed class UpdateIssueTool(
    IIssueRepository repository,
    ITaskRepository tasks,
    IWorkflowEngine workflowEngine,
    IWorkflowRepository workflowRepository,
    AgentToolScope scope) : IAgentTool
{
    public string Name => "update_issue";

    public string Description =>
        "Update an existing issue. Supply only the fields you want to change, for example to "
        + "re-triage severity or move the issue to Resolved. Send null for taskId or "
        + "assignedToUserId to clear them. Anna states the change and waits for the user's yes "
        + "before calling this; use ask_for_fields when it is unclear which fields should change.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "issueId": { "type": "integer", "description": "Id of the issue to update." },
            "title": { "type": "string", "description": "New title. Max 250 characters." },
            "description": { "type": "string", "description": "New description. Null clears it." },
            "severity": {
              "type": "string",
              "enum": ["Low", "Medium", "High", "Critical"],
              "description": "New severity."
            },
            "status": {
              "type": "string",
              "enum": ["Open", "InProgress", "Resolved", "Closed", "Rejected"],
              "description": "New status. Must be a legal transition from the current status."
            },
            "comment": {
              "type": "string",
              "description": "Optional note recorded with the status change (required for some transitions such as Blocked)."
            },
            "taskId": { "type": "integer", "description": "Link the issue to this task. Null unlinks it." },
            "assignedToUserId": { "type": "integer", "description": "New assignee. Null unassigns the issue." }
          },
          "required": ["issueId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.IssuesManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var issueId = ToolArguments.GetLong(arguments, "issueId");
        if (issueId is null or <= 0)
            return AgentToolResult.Fail("'issueId' is required and must be a positive issue id.");

        var issue = await repository.GetByIdAsync(issueId.Value, cancellationToken);
        if (issue is null || issue.IsDeleted)
            return AgentToolResult.Fail($"Issue {issueId} was not found.");

        if (!AgentToolScope.IsInScope(context, issue.ProjectId))
            return AgentToolResult.Fail($"Issue {issueId} belongs to another project and is out of scope for this conversation.");

        var previousStatus = issue.Status;
        var changed = false;
        var comment = ToolArguments.GetString(arguments, "comment");

        if (ToolArguments.Has(arguments, "title"))
        {
            var title = ToolArguments.GetString(arguments, "title");
            if (AgentToolScope.ValidateTitle(title) is { } titleError)
                return AgentToolResult.Fail(titleError);

            issue.Title = title!;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "description"))
        {
            issue.Description = ToolArguments.GetString(arguments, "description");
            changed = true;
        }

        if (ToolArguments.Has(arguments, "severity"))
        {
            if (!ToolArguments.TryGetEnum<IssueSeverity>(arguments, "severity", out var severity, out var severityError))
                return AgentToolResult.Fail(severityError!);

            issue.Severity = severity!.Value;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "assignedToUserId"))
        {
            var assigneeId = ToolArguments.GetLong(arguments, "assignedToUserId");
            if (await scope.ValidateUserAsync(assigneeId, "assignedToUserId", cancellationToken) is { } assigneeError)
                return AgentToolResult.Fail(assigneeError);

            issue.AssignedToUserId = assigneeId;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "taskId"))
        {
            if (ToolArguments.Has(arguments, "taskId"))
            {
                var taskId = ToolArguments.GetLong(arguments, "taskId");
                if (taskId is null or <= 0)
                    return AgentToolResult.Fail("'taskId' must be a positive task id.");

                var task = await tasks.GetByIdAsync(taskId.Value, cancellationToken);
                if (task is null || task.IsDeleted)
                    return AgentToolResult.Fail($"Task {taskId} was not found.");

                if (task.ProjectId != issue.ProjectId)
                    return AgentToolResult.Fail($"Task {taskId} belongs to project {task.ProjectId}, not project {issue.ProjectId}.");

                issue.TaskId = task.Id;
            }
            else
            {
                issue.TaskId = null;
            }

            changed = true;
        }

        if (ToolArguments.Has(arguments, "status"))
        {
            if (!ToolArguments.TryGetEnum<IssueStatus>(arguments, "status", out var status, out var statusError))
                return AgentToolResult.Fail(statusError!);

            var next = status!.Value;
            if (!await TryApplyWorkflowAsync(issue, previousStatus, next, comment, context.UserId, cancellationToken))
            {
                if (!IssueStatusRules.IsAllowed(previousStatus, next))
                    return AgentToolResult.Fail($"Issue status cannot change from {previousStatus} to {next}.");

                issue.Status = next;
            }

            changed = true;
        }

        if (!changed)
            return AgentToolResult.Fail("No updatable fields were supplied. Provide at least one of: title, description, severity, status, taskId, assignedToUserId.");

        issue.UpdateDate = DateTime.UtcNow;
        issue.UpdatedBy = context.UserId;

        // Same resolution bookkeeping IssueService performs.
        issue.ResolvedDate = issue.Status is IssueStatus.Resolved or IssueStatus.Closed
            ? issue.ResolvedDate ?? DateTime.UtcNow
            : previousStatus is IssueStatus.Resolved or IssueStatus.Closed
                ? null
                : issue.ResolvedDate;

        if (!await repository.UpdateAsync(issue, cancellationToken))
            return AgentToolResult.Fail($"Issue {issueId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = issue.Id,
            projectId = issue.ProjectId,
            taskId = issue.TaskId,
            title = issue.Title,
            description = issue.Description,
            severity = issue.Severity.ToString(),
            status = issue.Status.ToString(),
            reportedByUserId = issue.ReportedByUserId,
            assignedToUserId = issue.AssignedToUserId,
            resolvedDate = issue.ResolvedDate
        });
    }

    /// <summary>
    /// Routes the status change through the workflow engine when the project has a scheme; returns
    /// false to let the caller fall back to the static rules otherwise.
    /// </summary>
    private async Task<bool> TryApplyWorkflowAsync(
        Issue issue, IssueStatus previous, IssueStatus next, string? comment, long userId, CancellationToken cancellationToken)
    {
        var workflow = await workflowRepository.GetWorkflowByProjectAsync(issue.ProjectId, cancellationToken);
        if (workflow is null) return false;

        var fromCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Issue, previous);
        var toCode = WorkflowStatusMap.GetStatusCode(WorkflowEntityKind.Issue, next);
        if (fromCode == toCode) return false;

        var result = await workflowEngine.ExecuteAsync(WorkflowEntityKind.Issue, issue.ProjectId, issue.Id, fromCode, toCode,
            new Dictionary<string, object?> { ["comment"] = comment, ["assigneeUserId"] = issue.AssignedToUserId }, cancellationToken);

        issue.Status = next;
        issue.WorkflowStatusId = result.ToStatusId;

        await workflowRepository.SaveHistoryAsync(new IssueHistory
        {
            EntityType = "Issue",
            EntityId = issue.Id,
            WorkflowTransitionId = result.Transition.Id,
            FieldName = "Status",
            OldValue = previous.ToString(),
            NewValue = next.ToString(),
            Comment = comment,
            ChangedByUser = userId,
            InsertedBy = userId
        }, cancellationToken);

        await workflowRepository.StampStatusAsync("Issue", issue.Id, result.ToStatusId, userId, cancellationToken);
        return true;
    }
}
