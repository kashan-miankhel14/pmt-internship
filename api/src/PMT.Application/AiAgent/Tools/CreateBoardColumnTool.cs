using System.Text.Json;
using PMT.Application.Boards;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Adds a column to a project's board, for example a "QA" lane before Done.</summary>
/// <remarks>
/// <para>Keyed by projectKey, like the REST surface it mirrors
/// (<c>POST api/v1/projects/{projectKey}/columns</c>) and like the other board tools;
/// <see cref="ProjectKeyResolver"/> turns the key into the surrogate id
/// <see cref="IBoardColumnRepository"/> takes and applies the conversation's project pin.</para>
/// <para>The board is read before the insert for the same two reasons
/// <c>BoardColumnService.CreateAsync</c> reads it: a duplicate name comes back as a sentence the
/// model can relay instead of UQ_boardcolumns_project_name surfacing from the database, and an
/// omitted ordinal can be resolved to "one past the rightmost column" rather than defaulting to
/// 0 and silently landing the new column at the far left of the board.</para>
/// </remarks>
public sealed class CreateBoardColumnTool(
    IBoardColumnRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    /// <summary>The bound UpsertBoardColumnValidator applies to the same field on the REST surface.</summary>
    private const int MaxNameLength = 60;

    public string Name => "create_board_column";

    public string Description =>
        "Add a new column to a project's board, for example a 'QA' column. Give the project's key "
        + "(for example 'PMT') and the column name; the name must not already be used on that board. "
        + "Ordinal is the left-to-right position and defaults to the far right of the board, and "
        + "completeColumn marks the column that means done. Use list_board_columns first to see the "
        + "current board.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project the column belongs to, for example 'PMT'. Resolve it with search_projects." },
            "name": { "type": "string", "description": "Column name, for example 'QA'. Max 60 characters and unique within the project." },
            "ordinal": { "type": "integer", "description": "Left-to-right position. Lower comes first. Must not be negative. Defaults to the right-hand end of the board." },
            "completeColumn": { "type": "boolean", "description": "True when work in this column counts as done. Defaults to false." }
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
            return AgentToolResult.Fail("'name' is required. Ask the user what the column should be called.");

        if (name.Length > MaxNameLength)
            return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

        var columns = (await repository.GetByProjectAsync(projectId, cancellationToken))
            .Where(x => !x.IsDeleted)
            .ToArray();

        if (columns.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return AgentToolResult.Fail($"A board column named '{name}' already exists on this project.");

        int ordinal;
        if (ToolArguments.Has(arguments, "ordinal"))
        {
            var supplied = ToolArguments.GetIntOrNull(arguments, "ordinal");
            if (supplied is null or < 0)
                return AgentToolResult.Fail("'ordinal' must be a whole number of 0 or more.");

            ordinal = supplied.Value;
        }
        else
        {
            // A column the user did not place belongs at the end of the board, not in front of
            // everything: SP_BOARD_COLUMN's own ISNULL(@Ordinal, 0) would put it first.
            ordinal = columns.Select(x => x.Ordinal).DefaultIfEmpty(0).Max() + 1;
        }

        var completeColumn = false;
        if (ToolArguments.Has(arguments, "completeColumn")
            && !TryGetBoolean(arguments, "completeColumn", out completeColumn))
            return AgentToolResult.Fail("'completeColumn' must be true or false.");

        var entity = new BoardColumn
        {
            ProjectId = projectId,
            Name = name,
            Ordinal = ordinal,
            CompleteColumn = completeColumn,
            Active = true,
            CreatedBy = context.UserId,
            InsertedBy = context.UserId
        };

        var id = await repository.CreateAsync(entity, cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The board column could not be created.");

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            projectId,
            name = entity.Name,
            ordinal = entity.Ordinal,
            completeColumn = entity.CompleteColumn
        });
    }

    /// <summary>
    /// Reads a boolean a model may have written as a JSON boolean, as "true"/"false" text, or as
    /// 1/0. <see cref="ToolArguments"/> has no boolean reader, so the board tools carry their own.
    /// </summary>
    private static bool TryGetBoolean(JsonElement arguments, string name, out bool value)
    {
        value = false;

        var raw = ToolArguments.GetString(arguments, name);
        if (raw is null) return false;

        if (bool.TryParse(raw, out var parsed))
        {
            value = parsed;
            return true;
        }

        switch (raw)
        {
            case "1":
                value = true;
                return true;
            case "0":
                value = false;
                return true;
            default:
                return false;
        }
    }
}
