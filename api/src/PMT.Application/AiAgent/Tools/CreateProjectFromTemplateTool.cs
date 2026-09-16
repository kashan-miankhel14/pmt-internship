using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Departments;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Creates a project the way the project wizard does, through
/// <c>dbo.usp_Project_CreateFromTemplate</c>, so the new project arrives complete.
/// </summary>
/// <remarks>
/// <para><b>This is the creation path Anna should use.</b> <see cref="CreateProjectTool"/> writes a
/// bare row through SP_PROJECT's INSERT action: it carries no TypeCode, no AccessLevel and no lead,
/// so the project has no template to seed a board from, no workflow scheme and nobody holding
/// Project Admin on it. The template procedure — the one behind
/// <c>POST api/v1/projects/from-template</c> — inserts the project, seeds its ProjectCounters row
/// (the issue-key counter), grants the lead (and the creator, when they differ) Project Admin and
/// writes the audit entry, all in one transaction.</para>
/// <para>Called through <see cref="IProjectRepository.CreateFromTemplateAsync"/> rather than
/// through <c>ProjectService</c> because every other agent tool talks to repositories directly and
/// stamps the acting user from <see cref="AgentToolContext.UserId"/>, instead of depending on
/// <c>ICurrentUserService</c>. The rules <c>CreateProjectFromTemplateValidator</c> applies to the
/// REST payload are re-applied here so a project Anna creates is rejected for the same reasons a
/// project the wizard creates would be — including the 2-10 character key, which is what
/// dbo.Project.[Key] (varchar(10)) actually accepts.</para>
/// <para><b>departmentId, startDate and targetDate are applied after the fact.</b> The stored
/// procedure takes none of them: it inherits the creator's department and leaves the dates null.
/// When the user supplied any of the three, the freshly created project is read back and written
/// once through <see cref="IProjectRepository.UpdateAsync"/> — the same SP_PROJECT UPDATE the REST
/// <c>PUT api/v1/projects/{id}</c> performs, which coalesces the columns it is not given so the
/// TypeCode, AccessLevel and LeadUserId the template just set are preserved. A failure there is
/// reported alongside the created project rather than as a failed creation, because the project
/// itself exists and is already seeded.</para>
/// </remarks>
public sealed class CreateProjectFromTemplateTool(
    IProjectRepository repository,
    IProjectAccessRepository projectAccess,
    IDepartmentRepository departments,
    AgentToolScope scope) : IAgentTool
{
    /// <summary>dbo.Project.[Key] is varchar(10); CreateProjectFromTemplateValidator matches this.</summary>
    private const int MaxKeyLength = 10;

    private const int MinKeyLength = 2;

    /// <summary>The template validator's bound on Name.</summary>
    private const int MaxNameLength = 150;

    /// <summary>The template validator's bound on Description.</summary>
    private const int MaxDescriptionLength = 4000;

    private static readonly string[] TypeCodes = ["SCRUM", "KANBAN", "BASIC"];

    private static readonly string[] AccessLevels = ["OPEN", "RESTRICTED", "PRIVATE"];

    public string Name => "create_project_from_template";

    public string Description =>
        "Create a new project properly, the same way the project wizard does: the board columns, "
        + "workflow, issue-number counter and the lead's Project Admin access are all set up with it. "
        + "Prefer this over create_project. You must have typeCode (SCRUM, KANBAN or BASIC), "
        + "accessLevel (OPEN, RESTRICTED or PRIVATE), a 2-10 character uppercase key, a name and the "
        + "leadUserId of the person who will run the project; resolve the lead with search_users and "
        + "the optional departmentId with search_departments, and ask for anything missing with "
        + "ask_for_fields before calling this.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "typeCode": {
              "type": "string",
              "enum": ["SCRUM", "KANBAN", "BASIC"],
              "description": "Project template. SCRUM and BASIC seed To Do / In Progress / Done; KANBAN seeds Backlog / Selected / In Progress / Review / Done."
            },
            "accessLevel": {
              "type": "string",
              "enum": ["OPEN", "RESTRICTED", "PRIVATE"],
              "description": "Who can see the project. Ask the user if they have not said."
            },
            "key": { "type": "string", "description": "Short unique project key, 2-10 uppercase letters or digits, for example 'PMT'." },
            "name": { "type": "string", "description": "Project name. Max 150 characters." },
            "description": { "type": "string", "description": "What the project is for. Max 4000 characters." },
            "leadUserId": { "type": "integer", "description": "Id of the project lead, who is granted Project Admin. Resolve it with search_users." },
            "departmentId": { "type": "integer", "description": "Department that owns the project. Resolve it with search_departments. Defaults to the creator's department." },
            "startDate": { "type": "string", "description": "Start date as YYYY-MM-DD." },
            "targetDate": { "type": "string", "description": "Target completion date as YYYY-MM-DD. Must be on or after startDate." }
          },
          "required": ["typeCode", "accessLevel", "key", "name", "leadUserId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!TryGetChoice(arguments, "typeCode", TypeCodes, out var typeCode, out var typeError))
            return AgentToolResult.Fail(typeError!);

        if (!TryGetChoice(arguments, "accessLevel", AccessLevels, out var accessLevel, out var accessError))
            return AgentToolResult.Fail(accessError!);

        var key = ToolArguments.GetString(arguments, "key")?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(key))
            return AgentToolResult.Fail("'key' is required. Ask the user for a short project key such as 'PMT'.");

        if (key.Length is < MinKeyLength or > MaxKeyLength || !key.All(char.IsAsciiLetterOrDigit))
            return AgentToolResult.Fail($"'key' must be {MinKeyLength}-{MaxKeyLength} uppercase letters or digits, for example 'PMT'.");

        var name = ToolArguments.GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
            return AgentToolResult.Fail("'name' is required. Ask the user what the project should be called.");

        if (name.Length > MaxNameLength)
            return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

        var description = ToolArguments.GetString(arguments, "description");
        if (description is { Length: > MaxDescriptionLength })
            return AgentToolResult.Fail($"'description' must be {MaxDescriptionLength} characters or fewer.");

        var leadUserId = ToolArguments.GetLong(arguments, "leadUserId");
        if (leadUserId is null or <= 0)
            return AgentToolResult.Fail("'leadUserId' is required and must be a positive user id. Use search_users to resolve the project lead.");

        if (await scope.ValidateUserAsync(leadUserId, "leadUserId", cancellationToken) is { } leadError)
            return AgentToolResult.Fail(leadError);

        var departmentId = ToolArguments.GetLong(arguments, "departmentId");
        if (departmentId is not null)
        {
            if (departmentId <= 0)
                return AgentToolResult.Fail("'departmentId' must be a positive department id. Use search_departments to resolve it.");

            var department = await departments.GetByIdAsync(departmentId.Value, cancellationToken);
            if (department is null || department.IsDeleted)
                return AgentToolResult.Fail($"Department {departmentId} was not found. Use search_departments to resolve it.");
        }

        if (!ProjectKeyResolver.TryGetDateOnly(arguments, "startDate", out var startDate, out var startError))
            return AgentToolResult.Fail(startError!);

        if (!ProjectKeyResolver.TryGetDateOnly(arguments, "targetDate", out var targetDate, out var targetError))
            return AgentToolResult.Fail(targetError!);

        if (startDate is { } from && targetDate is { } to && to < from)
            return AgentToolResult.Fail("'targetDate' must be on or after 'startDate'.");

        // The procedure throws on a duplicate key, which would reach the model as the
        // orchestrator's generic "tool failed" text; checking first turns it into an
        // instruction the model can act on.
        if (await projectAccess.GetProjectIdByKeyAsync(key) is > 0)
            return AgentToolResult.Fail($"Another project already uses the key '{key}'. Pick a different key.");

        var id = await repository.CreateFromTemplateAsync(
            key,
            name,
            description,
            typeCode!,
            accessLevel!,
            leadUserId.Value,
            context.UserId,
            cancellationToken);

        if (id <= 0)
            return AgentToolResult.Fail("The project could not be created.");

        var (detailsApplied, detailsNote) = await ApplyOptionalDetailsAsync(
            context, id, departmentId, startDate, targetDate, cancellationToken);

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            key,
            name,
            typeCode,
            accessLevel,
            leadUserId = leadUserId.Value,
            departmentId,
            startDate = startDate?.ToString("yyyy-MM-dd"),
            targetDate = targetDate?.ToString("yyyy-MM-dd"),
            detailsApplied,
            note = detailsNote
                ?? "The board columns, workflow, issue counter and the lead's Project Admin access were set up with the project."
        });
    }

    /// <summary>
    /// Writes the three fields the template procedure does not accept. Returns false with a note
    /// when the follow-up write fails, so the model reports a created-but-incomplete project
    /// rather than claiming the whole creation failed.
    /// </summary>
    private async Task<(bool Applied, string? Note)> ApplyOptionalDetailsAsync(
        AgentToolContext context,
        long projectId,
        long? departmentId,
        DateOnly? startDate,
        DateOnly? targetDate,
        CancellationToken cancellationToken)
    {
        if (departmentId is null && startDate is null && targetDate is null)
            return (false, null);

        var project = await repository.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return (false, $"Project {projectId} was created, but the department and dates could not be applied because it could not be read back.");

        project.DepartmentId = departmentId ?? project.DepartmentId;
        project.StartDate = startDate ?? project.StartDate;
        project.TargetDate = targetDate ?? project.TargetDate;
        project.UpdateDate = DateTime.UtcNow;
        project.UpdatedBy = context.UserId;

        return await repository.UpdateAsync(project, cancellationToken)
            ? (true, null)
            : (false, $"Project {projectId} was created, but the department and dates could not be applied. Set them with update_project.");
    }

    /// <summary>
    /// Reads a required string choice. The two template fields are plain strings on the stored
    /// procedure rather than domain enums, so <see cref="ToolArguments.TryGetEnum{TEnum}"/> has
    /// nothing to bind to; the tolerance it applies (casing, spacing) is reproduced here.
    /// </summary>
    private static bool TryGetChoice(
        JsonElement arguments, string name, string[] allowed, out string? value, out string? error)
    {
        value = null;

        var raw = ToolArguments.GetString(arguments, name);
        if (raw is null)
        {
            error = $"'{name}' is required and must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        var normalized = raw.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToUpperInvariant();

        value = allowed.FirstOrDefault(x => x == normalized);
        if (value is null)
        {
            error = $"'{name}' must be one of: {string.Join(", ", allowed)}.";
            return false;
        }

        error = null;
        return true;
    }
}
