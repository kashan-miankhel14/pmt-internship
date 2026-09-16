using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Departments;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

public sealed class UpdateDepartmentTool(IDepartmentRepository repository) : IAgentTool
{
    public string Name => "update_department";
    public string Description =>
        "Update an existing department. Admin only. Anna states the change and waits for the user's "
        + "yes before calling this; use ask_for_fields when it is unclear which fields should change.";
    public string? RequiredPermission => PermissionRequirement.DepartmentsManage;
    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "departmentId": { "type": "integer" },
            "name": { "type": "string" },
            "code": { "type": "string" },
            "description": { "type": "string" }
          },
          "required": ["departmentId"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var deptId = ToolArguments.GetLong(arguments, "departmentId");
        if (deptId is null) return AgentToolResult.Fail("'departmentId' is required.");

        var dept = await repository.GetByIdAsync(deptId.Value, ct);
        if (dept is null || dept.IsDeleted) return AgentToolResult.Fail($"Department {deptId} was not found.");

        dept.Name = ToolArguments.GetString(arguments, "name") ?? dept.Name;
        dept.Code = ToolArguments.GetString(arguments, "code") ?? dept.Code;
        dept.Description = ToolArguments.GetString(arguments, "description") ?? dept.Description;
        dept.UpdateDate = DateTime.UtcNow;
        dept.UpdatedBy = context.UserId;

        if (!await repository.UpdateAsync(dept, ct))
            return AgentToolResult.Fail("The department could not be updated.");

        return AgentToolResult.Ok(new { updated = true, dept.Id, dept.Name });
    }
}
