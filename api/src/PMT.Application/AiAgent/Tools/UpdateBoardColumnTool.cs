using System.Text.Json;
using PMT.Application.Boards;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Renames a board column, moves it, or changes whether it counts as done.</summary>
/// <remarks>
/// <para><b>projectKey is required alongside columnId.</b> <see cref="IBoardColumnRepository"/>
/// has no get-by-id: the only read is <see cref="IBoardColumnRepository.GetByProjectAsync"/>, and
/// SP_BOARD_COLUMN's update action takes ProjectId as well as Id. So the column has to be located
/// through its project either way — which is the same round trip <c>BoardColumnService</c> makes,
/// and it is what lets a column id belonging to another project be reported as "not on this
/// board" instead of being written blindly.</para>
/// <para>The duplicate-name check mirrors the service's, so a clash comes back as a sentence the
/// model can relay rather than as UQ_boardcolumns_project_name surfacing from the database.</para>
/// </remarks>
public sealed class UpdateBoardColumnTool(
    IBoardColumnRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    /// <summary>The bound UpsertBoardColumnValidator applies to the same field on the REST surface.</summary>
    private const int MaxNameLength = 60;

    public string Name => "update_board_column";

    public string Description =>
        "Update one board column of a project: rename it, change its left-to-right ordinal, or set "
        + "whether it is the column that means done. Supply only the fields that should change. "
        + "Get the projectKey and columnId from list_board_columns first.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project the column belongs to, for example 'PMT'." },
            "columnId": { "type": "integer", "description": "Id of the board column to update. Get it from list_board_columns." },
            "name": { "type": "string", "description": "New column name. Max 60 characters and unique within the project." },
            "ordinal": { "type": "integer", "description": "New left-to-right position. Lower comes first. Must not be negative." },
            "completeColumn": { "type": "boolean", "description": "True when work in this column counts as done." }
          },
          "required": ["projectKey", "columnId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var columnId = ToolArguments.GetLong(arguments, "columnId");
        if (columnId is null or <= 0)
            return AgentToolResult.Fail("'columnId' is required and must be a positive column id. Use list_board_columns to find it.");

        var currentId = columnId.Value;

        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        var column = columns.FirstOrDefault(x => x.Id == currentId && !x.IsDeleted);
        if (column is null)
            return AgentToolResult.Fail($"Board column {columnId} was not found on this project's board.");

        var changed = false;

        if (ToolArguments.Has(arguments, "name"))
        {
            var name = ToolArguments.GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name))
                return AgentToolResult.Fail("'name' cannot be blank.");

            if (name.Length > MaxNameLength)
                return AgentToolResult.Fail($"'name' must be {MaxNameLength} characters or fewer.");

            // The column keeping its own name is not a clash.
            if (columns.Any(x => x.Id != currentId && !x.IsDeleted && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                return AgentToolResult.Fail($"A board column named '{name}' already exists on this project.");

            column.Name = name;
            changed = true;
        }

        if (ToolArguments.Has(arguments, "ordinal"))
        {
            var ordinal = ToolArguments.GetIntOrNull(arguments, "ordinal");
            if (ordinal is null or < 0)
                return AgentToolResult.Fail("'ordinal' must be a whole number of 0 or more.");

            column.Ordinal = ordinal.Value;
            changed = true;
        }

        if (ToolArguments.Has(arguments, "completeColumn"))
        {
            if (!TryGetBoolean(arguments, "completeColumn", out var completeColumn))
                return AgentToolResult.Fail("'completeColumn' must be true or false.");

            column.CompleteColumn = completeColumn;
            changed = true;
        }

        if (!changed)
            return AgentToolResult.Fail("No updatable fields were supplied. Provide at least one of: name, ordinal, completeColumn.");

        var entity = new BoardColumn
        {
            Id = currentId,
            ProjectId = projectId,
            Name = column.Name,
            Ordinal = column.Ordinal,
            CompleteColumn = column.CompleteColumn,
            UpdateDate = DateTime.UtcNow,
            UpdatedBy = context.UserId
        };

        if (!await repository.UpdateAsync(entity, cancellationToken))
            return AgentToolResult.Fail($"Board column {columnId} could not be updated.");

        return AgentToolResult.Ok(new
        {
            updated = true,
            id = entity.Id,
            projectId,
            name = entity.Name,
            ordinal = entity.Ordinal,
            completeColumn = entity.CompleteColumn
        });
    }

    /// <summary>
    /// Reads a boolean a model may have written as a JSON boolean, as "true"/"false" text, or as
    /// 1/0. <see cref="ToolArguments"/> has no boolean reader because no other tool takes one.
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
