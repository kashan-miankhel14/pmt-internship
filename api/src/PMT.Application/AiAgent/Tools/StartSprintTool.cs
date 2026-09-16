using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Starts a PLANNED sprint, setting its status to ACTIVE.</summary>
public sealed class StartSprintTool(ISprintRepository repository) : IAgentTool
{
    public string Name => "start_sprint";

    public string Description =>
        "Start a planned sprint, making it the active sprint for the project. "
        + "Only a sprint with status 'Planned' can be started. "
        + "Find the sprintId with search_sprints before calling this.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "sprintId": { "type": "integer", "description": "Id of the planned sprint to start. Get it from search_sprints." }
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

        if (sprint.Status != SprintStatus.Planned)
            return AgentToolResult.Fail(
                $"Sprint '{sprint.Name}' is {sprint.Status} and cannot be started. Only a planned sprint can be started.");

        var transitioned = await repository.StartAsync(sprint.Id, context.UserId, cancellationToken);
        if (transitioned <= 0)
            return AgentToolResult.Fail(
                $"Sprint '{sprint.Name}' could not be started because its status changed while I was working on it.");

        return AgentToolResult.Ok(new
        {
            started = true,
            id = sprint.Id,
            projectId = sprint.ProjectId,
            name = sprint.Name,
            status = SprintStatus.Active.ToString(),
            message = $"Sprint '{sprint.Name}' is now active!"
        });
    }
}
