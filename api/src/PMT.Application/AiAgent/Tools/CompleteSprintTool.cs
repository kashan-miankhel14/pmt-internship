using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Closes an ACTIVE sprint.</summary>
/// <remarks>
/// <para>Routed through <see cref="ISprintRepository.CompleteAsync"/> — the dedicated
/// <c>dbo.usp_Sprint_Complete</c> transition — rather than through a plain status write, so the
/// agent takes exactly the same path as <c>SprintsController.CompleteAsync</c> and inherits the
/// ACTIVE-only guard the procedure enforces. Setting <c>Status = 'COMPLETED'</c> with
/// update_sprint would skip whatever the transition does besides stamping the status.</para>
/// <para>The sprint is read first purely so "no such sprint" and "that sprint is not active" can
/// be told apart in the message the model reads back to the user, mirroring
/// <c>SprintService.CompleteAsync</c>. The procedure remains the authority: a status that changes
/// between the read and the call comes back as zero rows transitioned and is reported as such.</para>
/// </remarks>
public sealed class CompleteSprintTool(ISprintRepository repository) : IAgentTool
{
    public string Name => "complete_sprint";

    public string Description =>
        "Complete (close) an active sprint. Only a sprint that is currently Active can be completed. "
        + "Find the sprintId with search_sprints, name the sprint back to the user and get their yes "
        + "before calling this, because closing a sprint is not something they can undo from the chat.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "sprintId": { "type": "integer", "description": "Id of the active sprint to complete. Get it from search_sprints." }
          },
          "required": ["sprintId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var sprintId = ToolArguments.GetLong(arguments, "sprintId");
        if (sprintId is null or <= 0)
            return AgentToolResult.Fail("'sprintId' is required and must be a positive sprint id. Use search_sprints to find it.");

        var sprint = await repository.GetByIdAsync(sprintId.Value, cancellationToken);
        if (sprint is null || sprint.IsDeleted)
            return AgentToolResult.Fail("Sprint not found.");

        if (!AgentToolScope.IsInScope(context, sprint.ProjectId))
            return AgentToolResult.Fail($"Sprint {sprintId} belongs to another project and is out of scope for this conversation.");

        if (sprint.Status != SprintStatus.Active)
            return AgentToolResult.Fail(
                $"Sprint '{sprint.Name}' is {sprint.Status} and cannot be completed. Only an active sprint can be completed.");

        var transitioned = await repository.CompleteAsync(sprint.Id, context.UserId, cancellationToken);
        if (transitioned <= 0)
            return AgentToolResult.Fail(
                $"Sprint '{sprint.Name}' could not be completed because its status changed while I was working on it.");

        return AgentToolResult.Ok(new
        {
            completed = true,
            id = sprint.Id,
            projectId = sprint.ProjectId,
            name = sprint.Name,
            status = SprintStatus.Completed.ToString(),
            message = $"Sprint '{sprint.Name}' is now completed."
        });
    }
}
