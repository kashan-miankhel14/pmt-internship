using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Teams;
using PMT.Application.Teams.Dtos;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Creates a team. The key is immutable once issued, so it is validated here against the same
/// shape dbo.Teams enforces (CK_teams_key) rather than left for the database to reject.
/// </summary>
public sealed class CreateTeamTool(ITeamRepository repository) : IAgentTool
{
    /// <summary>Longest key varchar(10) accepts; the check constraint also requires at least two.</summary>
    private const int MaxKeyLength = 10;
    private const int MinKeyLength = 2;
    private const int MaxNameLength = 150;

    public string Name => "create_team";

    public string Description =>
        "Create a new team. Returns the created team including its new id. Anna must have the key, "
        + "name, and lead user id from the user before calling. Ask for anything missing with "
        + "ask_for_fields, then read the details back and wait for the user's yes.";

    // Teams have no permission key of their own: the REST surface gates every team write on
    // projects.manage, so the tool uses the same claim rather than inventing one no role holds.
    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "Short unique key, 2-10 characters, uppercase letters and digits only." },
            "name": { "type": "string", "description": "Team name. Max 150 characters." },
            "description": { "type": "string", "description": "Team description." },
            "leadUserId": { "type": "integer", "description": "Id of the user who leads the team. Defaults to the current user." }
          },
          "required": ["key", "name"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var key = ToolArguments.GetString(arguments, "key")?.ToUpperInvariant();
        if (ValidateKey(key) is { } keyError)
            return AgentToolResult.Fail(keyError);

        var name = ToolArguments.GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return AgentToolResult.Fail("'name' is required and cannot be blank.");
        if (name.Length > MaxNameLength)
            return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

        var leadUserId = ToolArguments.GetLong(arguments, "leadUserId") ?? context.UserId;
        if (leadUserId <= 0)
            return AgentToolResult.Fail("'leadUserId' must be a positive user id.");

        var request = new UpsertTeamRequest(
            key!,
            name,
            ToolArguments.GetString(arguments, "description"),
            leadUserId);

        var id = await repository.CreateAsync(request, context.UserId);
        if (id <= 0) return AgentToolResult.Fail("The team could not be created. The key may already be in use.");

        var created = await repository.GetByIdAsync(id);
        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            key = created?.Key ?? key,
            name = created?.Name ?? name
        });
    }

    /// <summary>Mirrors CK_teams_key: uppercase alphanumeric, at least two characters, at most ten.</summary>
    private static string? ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "'key' is required and cannot be blank.";

        if (key.Length is < MinKeyLength or > MaxKeyLength)
            return $"'key' must be between {MinKeyLength} and {MaxKeyLength} characters.";

        return key.All(char.IsAsciiLetterOrDigit)
            ? null
            : "'key' may contain only letters and digits.";
    }
}
