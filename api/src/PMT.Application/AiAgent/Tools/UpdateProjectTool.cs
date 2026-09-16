using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Tools;

public sealed class UpdateProjectTool(IProjectRepository repository, AgentToolScope scope) : IAgentTool
{
    public string Name => "update_project";
    public string Description =>
        "Update an existing project. Only provided fields are changed. Anna states the change and "
        + "waits for the user's yes before calling this; use ask_for_fields when it is unclear which "
        + "fields should change or what to.";
    public string? RequiredPermission => PermissionRequirement.ProjectsManage;
    public bool IsDestructive => false;

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectId": { "type": "integer" },
            "name": { "type": "string" },
            "description": { "type": "string" },
            "status": { "type": "string", "enum": ["Active","OnHold","Completed","Cancelled"] },
            "startDate": { "type": "string" },
            "targetDate": { "type": "string" }
          },
          "required": ["projectId"]
        }
        """;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken ct = default)
    {
        var projectId = ToolArguments.GetLong(arguments, "projectId");
        if (projectId is null) return AgentToolResult.Fail("'projectId' is required.");

        var (project, projectError) = await scope.ResolveProjectAsync(context, projectId.Value, ct);
        if (project is null) return AgentToolResult.Fail(projectError!);

        var name = ToolArguments.GetString(arguments, "name");
        if (name is not null && name.Length > 250) return AgentToolResult.Fail("'name' must be 250 characters or fewer.");

        project.Name = name ?? project.Name;
        project.Description = ToolArguments.GetString(arguments, "description") ?? project.Description;

        if (ToolArguments.TryGetEnum<ProjectStatus>(arguments, "status", out var status, out _))
            project.Status = status!.Value;

        if (ToolArguments.GetString(arguments, "startDate") is { Length: > 0 } sd && DateOnly.TryParse(sd, out var s))
            project.StartDate = s;
        if (ToolArguments.GetString(arguments, "targetDate") is { Length: > 0 } td && DateOnly.TryParse(td, out var t))
            project.TargetDate = t;

        project.UpdateDate = DateTime.UtcNow;
        project.UpdatedBy = context.UserId;

        if (!await repository.UpdateAsync(project, ct))
            return AgentToolResult.Fail("The project could not be updated.");

        return AgentToolResult.Ok(new { updated = true, project.Id, project.Key, project.Name });
    }
}
