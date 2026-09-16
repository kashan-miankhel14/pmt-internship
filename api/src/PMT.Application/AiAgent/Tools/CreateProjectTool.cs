using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

public sealed class CreateProjectTool(IProjectRepository repository) : IAgentTool
{
    public string Name => "create_project";
    public string Description =>
        "Create a bare project row. Prefer create_project_from_template, which is the wizard's path "
        + "and also seeds the board columns, workflow, issue counter and the lead's Project Admin "
        + "access; use this tool only when a plain project record is genuinely what is wanted. Anna "
        + "must have the key and name from the user before calling this, and must resolve "
        + "departmentId with search_departments rather than guessing or leaving it to a default: ask "
        + "for anything missing with ask_for_fields, then read the details back and wait for the "
        + "user's yes.";
    public string? RequiredPermission => PermissionRequirement.ProjectsManage;
    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "description": "Short unique key, max 15 chars, uppercase alphanumeric." },
            "name": { "type": "string", "description": "Project name. Max 250 characters." },
            "description": { "type": "string", "description": "Project description." },
            "ownerUserId": { "type": "integer", "description": "Id of the project owner." },
            "departmentId": { "type": "integer", "description": "Department this project belongs to." },
            "status": { "type": "string", "enum": ["Active","OnHold","Completed","Cancelled"], "description": "Project status. Defaults to Active." },
            "startDate": { "type": "string", "description": "Start date (YYYY-MM-DD)." },
            "targetDate": { "type": "string", "description": "Target completion date (YYYY-MM-DD)." }
          },
          "required": ["key", "name"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var key = ToolArguments.GetString(arguments, "key");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 15)
            return AgentToolResult.Fail("'key' is required and must be 15 characters or fewer.");

        var name = ToolArguments.GetString(arguments, "name");
        if (AgentToolScope.ValidateTitle(name) is { } error)
            return AgentToolResult.Fail(error);

        if (!ToolArguments.TryGetEnum<ProjectStatus>(arguments, "status", out var status, out var statusError))
            return AgentToolResult.Fail(statusError!);

        var ownerUserId = ToolArguments.GetLong(arguments, "ownerUserId") ?? context.UserId;
        // Default to department 1 (IT) which is seeded. 0 violates FK.
        var departmentId = ToolArguments.GetLong(arguments, "departmentId") ?? 1;

        DateOnly? startDate = null;
        DateOnly? targetDate = null;
        if (ToolArguments.GetString(arguments, "startDate") is { Length: > 0 } sd && DateOnly.TryParse(sd, out var s))
            startDate = s;
        if (ToolArguments.GetString(arguments, "targetDate") is { Length: > 0 } td && DateOnly.TryParse(td, out var t))
            targetDate = t;

        var entity = new Project
        {
            Key = key.ToUpperInvariant(),
            Name = name!,
            Description = ToolArguments.GetString(arguments, "description"),
            OwnerUserId = ownerUserId,
            DepartmentId = departmentId,
            Status = status ?? ProjectStatus.Active,
            StartDate = startDate,
            TargetDate = targetDate,
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, ct);
        if (id <= 0) return AgentToolResult.Fail("The project could not be created.");

        var created = await repository.GetByIdAsync(id, ct);
        return AgentToolResult.Ok(new { created = true, id, key = entity.Key, name = created?.Name ?? entity.Name });
    }
}
