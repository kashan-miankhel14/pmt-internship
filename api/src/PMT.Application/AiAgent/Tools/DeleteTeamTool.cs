using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes a team and every ProjectTeam grant it holds.</summary>
/// <remarks>
/// Destructive, so the orchestrator parks the call and only runs it after the user approves it
/// through /ai/agent/confirm. The <c>confirm</c> argument is a second, in-band guard: the model
/// has to state the intent explicitly rather than reach the delete by fumbling an argument.
/// Projects the team could reach are left untouched; only the grants disappear.
/// </remarks>
public sealed class DeleteTeamTool(ITeamRepository repository) : IAgentTool
{
    public string Name => "delete_team";

    public string Description =>
        "Soft delete a team. Removes all ProjectTeam grants but does NOT delete projects. "
        + "Resolve the exact teamId with search_teams first. The user is asked to confirm before "
        + "the deletion actually happens.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "teamId": { "type": "integer", "description": "Id of the team to delete." },
            "confirm": { "type": "boolean", "description": "Must be true. Set it only after the user has agreed to the deletion." }
          },
          "required": ["teamId", "confirm"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var teamId = ToolArguments.GetLong(arguments, "teamId");
        if (teamId is null or <= 0)
            return AgentToolResult.Fail("'teamId' is required and must be a positive team id.");

        // GetString normalises true/false to "true"/"false", so a boolean and the string form both read.
        if (!string.Equals(ToolArguments.GetString(arguments, "confirm"), "true", StringComparison.OrdinalIgnoreCase))
            return AgentToolResult.Fail("'confirm' must be true to delete a team.");

        var team = await repository.GetByIdAsync(teamId.Value);
        if (team is null) return AgentToolResult.Fail($"Team {teamId} was not found.");

        if (!await repository.DeleteAsync(team.Id))
            return AgentToolResult.Fail($"Team {teamId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"Team #{team.Id} ({team.Key}) was deleted. Its project grants were removed; no projects were deleted.",
            deleted = true,
            id = team.Id,
            key = team.Key,
            name = team.Name
        });
    }
}
