using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Sprints;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Creates a sprint inside a project addressed by its public key.</summary>
/// <remarks>
/// Keyed by projectKey rather than projectId because that is how the sprint REST surface is
/// addressed and how people talk about sprints ("a new sprint on PMT"); the key is resolved to
/// the surrogate id by <see cref="ProjectKeyResolver"/>, which also applies the conversation's
/// project pin. The limits enforced here are the ones
/// <c>UpsertSprintValidator</c> enforces on the same payload, so a sprint Anna creates and a
/// sprint the UI creates are rejected for the same reasons.
/// </remarks>
public sealed class CreateSprintTool(
    ISprintRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    /// <summary>Sprint.Name is varchar(150); UpsertSprintValidator enforces the same bound.</summary>
    private const int MaxNameLength = 150;

    /// <summary>Sprint.Goal is varchar(1000).</summary>
    private const int MaxGoalLength = 1000;

    public string Name => "create_sprint";

    public string Description =>
        "Create a sprint in a project. Give the project's key (for example 'PMT') and a sprint name; "
        + "goal, start date, end date and status are optional. New sprints are Planned unless you say "
        + "otherwise. Use search_projects first if you are not sure of the key, and ask_for_fields when "
        + "the user has not said what the sprint should be called.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." },
            "name": { "type": "string", "description": "Sprint name, for example 'Sprint 12'. Max 150 characters." },
            "goal": { "type": "string", "description": "What the sprint is meant to achieve. Max 1000 characters." },
            "startDate": { "type": "string", "description": "Start date as YYYY-MM-DD." },
            "endDate": { "type": "string", "description": "End date as YYYY-MM-DD. Must be on or after startDate." },
            "status": {
              "type": "string",
              "enum": ["Planned", "Active", "Completed"],
              "description": "Sprint status. Defaults to Planned."
            }
          },
          "required": ["projectKey", "name"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var name = ToolArguments.GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return AgentToolResult.Fail("'name' is required. Ask the user what the sprint should be called.");

        if (name.Length > MaxNameLength)
            return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

        var goal = ToolArguments.GetString(arguments, "goal");
        if (goal is { Length: > MaxGoalLength })
            return AgentToolResult.Fail($"'goal' must be {MaxGoalLength} characters or fewer.");

        if (!ProjectKeyResolver.TryGetDateOnly(arguments, "startDate", out var startDate, out var startError))
            return AgentToolResult.Fail(startError!);

        if (!ProjectKeyResolver.TryGetDateOnly(arguments, "endDate", out var endDate, out var endError))
            return AgentToolResult.Fail(endError!);

        if (startDate is { } from && endDate is { } to && to < from)
            return AgentToolResult.Fail("'endDate' must be on or after 'startDate'.");

        if (!ToolArguments.TryGetEnum<SprintStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var entity = new Sprint
        {
            ProjectId = projectId,
            Name = name,
            Goal = goal,
            StartDate = startDate,
            EndDate = endDate,
            Status = status ?? SprintStatus.Planned,
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The sprint could not be created.");

        var created = await repository.GetByIdAsync(id, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            projectId,
            name = created?.Name ?? entity.Name,
            goal = created?.Goal ?? entity.Goal,
            status = (created?.Status ?? entity.Status).ToString(),
            startDate = (created?.StartDate ?? entity.StartDate)?.ToString("yyyy-MM-dd"),
            endDate = (created?.EndDate ?? entity.EndDate)?.ToString("yyyy-MM-dd")
        });
    }
}
