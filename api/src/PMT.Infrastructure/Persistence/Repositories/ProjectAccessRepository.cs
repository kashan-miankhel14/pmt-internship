using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper-backed <see cref="IProjectAccessRepository"/> covering direct project membership
/// (SP_PROJECT_MEMBER), team grants (SP_PROJECT_TEAM) and effective role resolution
/// (usp_Project_EffectiveRole).
/// </summary>
/// <remarks>
/// <see cref="ICurrentUserService"/> is injected because the removal members of
/// <see cref="IProjectAccessRepository"/> do not carry an actor argument; without it the
/// DeletedBy audit column would always be NULL.
/// </remarks>
public sealed class ProjectAccessRepository(IDbConnectionFactory connectionFactory, ICurrentUserService currentUser) : IProjectAccessRepository
{
    public async Task<long?> GetProjectIdByKeyAsync(string projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            return null;

        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Project),
            new { Action = ProcedureNames.Action(ProcedureAction.FetchByKey), Key = projectKey.Trim().ToUpperInvariant() },
            commandType: CommandType.StoredProcedure));
    }

    public async Task<PagedResult<ProjectMemberDto>> ListMembersAsync(long projectId, int page, int pageSize, string? search)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new
        {
            Action = ProcedureNames.Action(ProcedureAction.Paged),
            ProjectId = projectId,
            Search = normalizedSearch,
            Page = page,
            PageSize = pageSize
        };

        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.ProjectMember), args, commandType: CommandType.StoredProcedure));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<ProjectMemberDto>()).AsList();
        return new PagedResult<ProjectMemberDto>(rows, page, pageSize, total);
    }

    public async Task<bool> AddMemberAsync(long projectId, long userId, long projectRoleId, long addedBy)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.ProjectMember),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                ProjectId = projectId,
                UserId = userId,
                ProjectRoleId = projectRoleId,
                AddedBy = addedBy
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<bool> RemoveMemberAsync(long projectId, long userId)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.ProjectMember),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Delete),
                ProjectId = projectId,
                UserId = userId,
                Actor = currentUser.UserId
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<PagedResult<ProjectTeamDto>> ListTeamGrantsAsync(long projectId, int page, int pageSize)
    {
        var args = new
        {
            Action = ProcedureNames.Action(ProcedureAction.Paged),
            ProjectId = projectId,
            Page = page,
            PageSize = pageSize
        };

        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.ProjectTeam), args, commandType: CommandType.StoredProcedure));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<ProjectTeamDto>()).AsList();
        return new PagedResult<ProjectTeamDto>(rows, page, pageSize, total);
    }

    public async Task<bool> AddTeamGrantAsync(long projectId, long teamId, long projectRoleId, long addedBy)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.ProjectTeam),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                ProjectId = projectId,
                TeamId = teamId,
                ProjectRoleId = projectRoleId,
                AddedBy = addedBy
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<bool> RemoveTeamGrantAsync(long projectId, long teamId)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.ProjectTeam),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Delete),
                ProjectId = projectId,
                TeamId = teamId,
                Actor = currentUser.UserId
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    /// <summary>
    /// Returns the most privileged role the user holds on the project, or <c>null</c> when
    /// they hold none. The procedure already collapses direct membership and team grants
    /// and orders by SortOrder, so the first row wins.
    /// </summary>
    public async Task<EffectiveRoleDto?> GetEffectiveRoleAsync(long projectId, long userId)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<EffectiveRoleDto>(new CommandDefinition(
            ProcedureNames.ProjectEffectiveRole,
            new { ProjectId = projectId, UserId = userId },
            commandType: CommandType.StoredProcedure));
    }
}
