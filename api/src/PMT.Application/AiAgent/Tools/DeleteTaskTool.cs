using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Tasks;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes a task the caller can reach.</summary>
/// <remarks>
/// Destructive, so the orchestrator pauses the turn and asks the user to approve the removal
/// before this tool is ever executed. The delete itself is the same soft delete the UI performs.
/// </remarks>
public sealed class DeleteTaskTool(ITaskRepository repository) : IAgentTool
{
    public string Name => "delete_task";

    public string Description =>
        "Delete a task. Resolve the exact taskId with search_tasks or get_entity first. "
        + "The user is asked to confirm before the deletion actually happens.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "taskId": { "type": "integer", "description": "Id of the task to delete." }
          },
          "required": ["taskId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.TasksManage;

    public bool IsDestructive => true;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var taskId = ToolArguments.GetLong(arguments, "taskId");
        if (taskId is null or <= 0)
            return AgentToolResult.Fail("'taskId' is required and must be a positive task id.");

        var task = await repository.GetByIdAsync(taskId.Value, cancellationToken);
        if (task is null || task.IsDeleted)
            return AgentToolResult.Fail($"Task {taskId} was not found.");

        if (!AgentToolScope.IsInScope(context, task.ProjectId))
            return AgentToolResult.Fail($"Task {taskId} belongs to another project and is out of scope for this conversation.");

        if (!await repository.DeleteAsync(task.Id, context.UserId, cancellationToken))
            return AgentToolResult.Fail($"Task {taskId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = task.Id,
            title = task.Title,
            projectId = task.ProjectId
        });
    }
}
