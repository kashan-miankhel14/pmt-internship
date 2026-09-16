using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Issues;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes an issue the caller can reach.</summary>
/// <remarks>
/// Destructive, so the orchestrator pauses the turn and asks the user to approve the removal
/// before this tool is ever executed. The delete itself is the same soft delete the UI performs.
/// </remarks>
public sealed class DeleteIssueTool(IIssueRepository repository) : IAgentTool
{
    public string Name => "delete_issue";

    public string Description =>
        "Delete an issue. Resolve the exact issueId with search_issues or get_entity first. "
        + "The user is asked to confirm before the deletion actually happens.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "issueId": { "type": "integer", "description": "Id of the issue to delete." }
          },
          "required": ["issueId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.IssuesManage;

    public bool IsDestructive => true;

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

        if (!await repository.DeleteAsync(issue.Id, context.UserId, cancellationToken))
            return AgentToolResult.Fail($"Issue {issueId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = issue.Id,
            title = issue.Title,
            projectId = issue.ProjectId
        });
    }
}
