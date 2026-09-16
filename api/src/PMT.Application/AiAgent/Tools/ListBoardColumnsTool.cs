using System.Text.Json;
using PMT.Application.Boards;
using PMT.Application.Common.Security;
using PMT.Application.Projects;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads one project's board configuration, left to right.</summary>
/// <remarks>
/// The board is the vocabulary a team uses for "where is this work?", so Anna needs it before
/// she can answer a question phrased in a project's own column names rather than in the fixed
/// status enums. SP_BOARD_COLUMN already returns the rows in Ordinal order; the ordering is
/// re-applied here so the payload is board order regardless of what the procedure does later.
/// </remarks>
public sealed class ListBoardColumnsTool(
    IBoardColumnRepository repository,
    IProjectAccessRepository projectAccess) : IAgentTool
{
    public string Name => "list_board_columns";

    public string Description =>
        "List a project's board columns in left-to-right order, with their ids, ordinals and which "
        + "column counts as done. Give the project's key (for example 'PMT'). Use this to answer "
        + "questions about a project's board and to get the columnId for update_board_column.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "projectKey": { "type": "string", "description": "Public key of the project, for example 'PMT'. Resolve it with search_projects." }
          },
          "required": ["projectKey"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.ProjectsView;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var (projectId, projectError) = await ProjectKeyResolver.ResolveAsync(projectAccess, context, arguments);
        if (projectError is not null) return AgentToolResult.Fail(projectError);

        var columns = await repository.GetByProjectAsync(projectId, cancellationToken);

        var results = columns
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                ordinal = x.Ordinal,
                completeColumn = x.CompleteColumn
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            projectId,
            columns = results
        });
    }
}
