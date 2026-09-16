using System.Text.Json;
using PMT.Application.Boards;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Soft-deletes one column from a project's board.</summary>
/// <remarks>
/// <para>This tool is <see cref="IsDestructive"/>: the orchestrator never runs it straight off the
/// model's request. The turn is paused, the user is shown which column is about to be removed, and
/// the call only reaches <see cref="ExecuteAsync"/> after an explicit approval.</para>
/// <para><b>projectKey is required alongside columnId</b>, exactly as in
/// <see cref="UpdateBoardColumnTool"/> and in the REST route this mirrors
/// (<c>DELETE api/v1/projects/{projectKey}/columns/{id}</c>): <see cref="IBoardColumnRepository"/>
/// has no get-by-id, so the column is located through its project's board, which is also what
/// turns a column id belonging to another project into "not on this board" instead of a blind
/// delete.</para>
/// <para>The delete is the same soft delete the UI performs (<c>IsDeleted</c> plus the deleting
/// user), so the row survives in SQL and its name is released for reuse.</para>
/// </remarks>
public sealed class DeleteBoardColumnTool(
    IBoardColumnRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    public string Name => "delete_board_column";

    public string Description =>
        "Delete a column from a project's board. Give the project's key (for example 'PMT') and the "
        + "columnId, which you get from list_board_columns. The user is asked to confirm before the "
        + "column is actually deleted.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project the column belongs to, for example 'PMT'." },
            "columnId": { "type": "integer", "description": "Id of the board column to delete. Get it from list_board_columns." }
          },
          "required": ["projectKey", "columnId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsManage;

    public bool IsDestructive => true;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var columnId = ToolArguments.GetLong(arguments, "columnId");
        if (columnId is null or <= 0)
            return AgentToolResult.Fail("'columnId' is required and must be a positive column id. Use list_board_columns to find it.");

        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);
        var column = columns.FirstOrDefault(x => x.Id == columnId.Value && !x.IsDeleted);
        if (column is null)
            return AgentToolResult.Fail($"Board column {columnId} was not found on this project's board.");

        if (!await repository.DeleteAsync(column.Id, context.UserId, cancellationToken))
            return AgentToolResult.Fail($"Board column {columnId} could not be deleted.");

        return AgentToolResult.Ok(new
        {
            deleted = true,
            id = column.Id,
            projectId,
            name = column.Name
        });
    }
}
