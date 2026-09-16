using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Departments;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

public sealed class CreateDepartmentTool(IDepartmentRepository repository) : IAgentTool
{
    public string Name => "create_department";
    public string Description =>
        "Create a new department. Admin only. Anna must have the department's details from the user "
        + "before calling this: ask for anything missing with ask_for_fields, then confirm.";
    public string? RequiredPermission => PermissionRequirement.DepartmentsManage;
    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Department name." },
            "code": { "type": "string", "description": "Short code, max 15 chars." },
            "description": { "type": "string", "description": "Department description." }
          },
          "required": ["name"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var name = ToolArguments.GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return AgentToolResult.Fail("'name' is required.");

        var entity = new Department
        {
            Name = name,
            Code = ToolArguments.GetString(arguments, "code") ?? string.Empty,
            Description = ToolArguments.GetString(arguments, "description"),
            Active = true,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, ct);
        if (id <= 0) return AgentToolResult.Fail("The department could not be created.");

        return AgentToolResult.Ok(new { created = true, id, name = entity.Name });
    }
}
