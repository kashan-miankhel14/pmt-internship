using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read-only access to the project role lookup. SP_PROJECT_ROLE exposes only a paged
/// reader, so the full list is fetched as a single oversized page.
/// </summary>
public sealed class ProjectRoleRepository(IDbConnectionFactory connectionFactory) : IProjectRoleRepository
{
    /// <summary>
    /// Project roles are a small, seeded lookup (three rows out of the box) that grows only
    /// when an administrator defines a custom role, so one page comfortably covers the table.
    /// </summary>
    private const int AllRolesPageSize = 1000;

    public async Task<IReadOnlyList<ProjectRoleDto>> ListAsync()
    {
        var args = new
        {
            Action = ProcedureNames.Action(ProcedureAction.Paged),
            Search = (string?)null,
            Page = 1,
            PageSize = AllRolesPageSize
        };

        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.ProjectRole), args, commandType: CommandType.StoredProcedure));

        // The PAGED action always emits the total count first; it is redundant here because
        // the single page is the whole table, but the grid must still be read in order.
        _ = await grid.ReadSingleAsync<long>();
        return (await grid.ReadAsync<ProjectRoleDto>()).AsList();
    }
}
