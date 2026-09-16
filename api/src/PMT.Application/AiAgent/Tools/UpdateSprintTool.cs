using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Sprints;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Applies a partial update to a sprint. Only the fields the model actually supplies are
/// touched; everything else keeps its stored value.
/// </summary>
/// <remarks>
/// <para>Addressed by sprint id alone, because that is what search_sprints hands back. The
/// project is read off the stored sprint rather than taken from the model, so a pinned
/// conversation cannot be talked into editing another project's sprint by supplying its id.</para>
/// <para><c>goal</c>, <c>startDate</c> and <c>endDate</c> are nullable columns, so they are read
/// with <see cref="ToolArguments.IsMentioned"/>: an explicit null clears them, an absent field
/// leaves them alone. The date pair is re-checked as a pair after the merge — moving only the
/// start date of an existing sprint can invert a range that was valid before the call.</para>
/// </remarks>
public sealed class UpdateSprintTool(ISprintRepository repository) : IAgentTool
{
    private const int MaxNameLength = 150;
    private const int MaxGoalLength = 1000;

    public string Name => "update_sprint";

    public string Description =>
        "Update an existing sprint: rename it, change its goal, move its dates, or change its status "
        + "between Planned, Active and Completed. Supply only the fields that should change. Send null "
        + "for goal, startDate or endDate to clear them. Find the sprintId with search_sprints first, "
        + "and say what you are about to change before you call this.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "sprintId": { "type": "integer", "description": "Id of the sprint to update. Get it from search_sprints." },
            "name": { "type": "string", "description": "New sprint name. Max 150 characters." },
            "goal": { "type": "string", "description": "New sprint goal. Null clears it. Max 1000 characters." },
            "startDate": { "type": "string", "description": "New start date as YYYY-MM-DD. Null clears it." },
            "endDate": { "type": "string", "description": "New end date as YYYY-MM-DD. Null clears it. Must be on or after startDate." },
            "status": {
              "type": "string",
              "enum": ["Planned", "Active", "Completed"],
              "description": "New sprint status. Use complete_sprint to close an active sprint properly."
            }
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
            return AgentToolResult.Fail($"Sprint {sprintId} was not found.");

        if (!AgentToolScope.IsInScope(context, sprint.ProjectId))
            return AgentToolResult.Fail($"Sprint {sprintId} belongs to another project and is out of scope for this conversation.");

        var changed = false;

        if (ToolArguments.Has(arguments, "name"))
        {
            var name = ToolArguments.GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name))
                return AgentToolResult.Fail("'name' cannot be blank.");

            if (name.Length > MaxNameLength)
                return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

            sprint.Name = name;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "goal"))
        {
            var goal = ToolArguments.GetString(arguments, "goal");
            if (goal is { Length: > MaxGoalLength })
                return AgentToolResult.Fail($"'goal' must be {MaxGoalLength} characters or fewer.");

            sprint.Goal = goal;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "startDate"))
        {
            if (!ProjectKeyResolver.TryGetDateOnly(arguments, "startDate", out var startDate, out var startError))
                return AgentToolResult.Fail(startError!);

            sprint.StartDate = startDate;
            changed = true;
        }

        if (ToolArguments.IsMentioned(arguments, "endDate"))
        {
            if (!ProjectKeyResolver.TryGetDateOnly(arguments, "endDate", out var endDate, out var endError))
                return AgentToolResult.Fail(endError!);

            sprint.EndDate = endDate;
            changed = true;
        }

        // Checked after the merge: a call that moves only one end of the range can still invert it.
        if (sprint.StartDate is { } from && sprint.EndDate is { } to && to < from)
            return AgentToolResult.Fail("'endDate' must be on or after 'startDate'.");

        if (ToolArguments.Has(arguments, "status"))
        {
            if (!ToolArguments.TryGetEnum<SprintStatus>(arguments, "status", out var status, out var statusError))
                return AgentToolResult.Fail(statusError!);

            sprint.Status = status!.Value;
            changed = true;
        }

        if (!changed)
            return AgentToolResult.Fail("No updatable fields were supplied. Provide at least one of: name, goal, startDate, endDate, status.");

        sprint.UpdateDate = DateTime.UtcNow;
        sprint.UpdatedBy = context.UserId;

        if (!await repository.UpdateAsync(sprint, cancellationToken))
            return AgentToolResult.Fail($"Sprint {sprintId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = sprint.Id,
            projectId = sprint.ProjectId,
            name = sprint.Name,
            goal = sprint.Goal,
            status = sprint.Status.ToString(),
            startDate = sprint.StartDate?.ToString("yyyy-MM-dd"),
            endDate = sprint.EndDate?.ToString("yyyy-MM-dd")
        });
    }
}
