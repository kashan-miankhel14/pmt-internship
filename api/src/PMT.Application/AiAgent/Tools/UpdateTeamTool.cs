using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;
using PMT.Application.Teams.Dtos;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Updates a team's mutable fields. SP_TEAM's UPDATE branch ignores @Key, so the current key is
/// re-sent unchanged and a key the model supplies is deliberately not honoured.
/// </summary>
public sealed class UpdateTeamTool(ITeamRepository repository) : IAgentTool
{
    private const int MaxNameLength = 150;

    public string Name => "update_team";

    public string Description =>
        "Update a team's name, description, or lead. Key cannot be changed. Only provided fields "
        + "are changed. Anna states the change and waits for the user's yes before calling this.";

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "teamId": { "type": "integer", "description": "Id of the team to update." },
            "name": { "type": "string", "description": "New team name. Max 150 characters." },
            "description": { "type": "string", "description": "New team description." },
            "leadUserId": { "type": "integer", "description": "Id of the user who leads the team." }
          },
          "required": ["teamId"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var teamId = ToolArguments.GetLong(arguments, "teamId");
        if (teamId is null or <= 0)
            return AgentToolResult.Fail("'teamId' is required and must be a positive team id.");

        var team = await repository.GetByIdAsync(teamId.Value);
        if (team is null) return AgentToolResult.Fail($"Team {teamId} was not found.");

        var name = ToolArguments.GetString(arguments, "name") ?? team.Name;
        if (name.Length > MaxNameLength)
            return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

        var leadUserId = ToolArguments.GetLong(arguments, "leadUserId") ?? team.LeadUserId;
        if (leadUserId <= 0)
            return AgentToolResult.Fail("'leadUserId' must be a positive user id.");

        // The key is immutable, so it is echoed back rather than read from the arguments.
        // Active is echoed back too: this tool has no switch for it, and the request defaults
        // to true, which would otherwise quietly reactivate a deactivated team.
        var request = new UpsertTeamRequest(
            team.Key,
            name,
            ToolArguments.GetString(arguments, "description") ?? team.Description,
            leadUserId,
            team.IsActive);

        if (!await repository.UpdateAsync(team.Id, request))
            return AgentToolResult.Fail($"Team {teamId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"Team #{team.Id} ({team.Key}) was updated.",
            id = team.Id,
            key = team.Key,
            name
        });
    }
}
