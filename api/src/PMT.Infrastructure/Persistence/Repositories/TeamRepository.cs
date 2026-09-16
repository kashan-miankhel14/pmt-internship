using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Teams;
using PMT.Application.Teams.Dtos;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper-backed <see cref="ITeamRepository"/>. Every call is a single stored procedure
/// invocation against SP_TEAM / SP_TEAM_MEMBER, dispatched by the @Action argument.
/// </summary>
/// <remarks>
/// <see cref="ICurrentUserService"/> is injected because the mutating members of
/// <see cref="ITeamRepository"/> (update, delete, remove member) do not carry an actor
/// argument. Without it the audit columns (UpdatedBy / DeletedBy) would be written as NULL
/// on every one of those operations.
/// </remarks>
public sealed class TeamRepository(IDbConnectionFactory connectionFactory, ICurrentUserService currentUser) : ITeamRepository
{
    public async Task<PagedResult<TeamDto>> ListAsync(int page, int pageSize, string? search)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new { Action = ProcedureNames.Action(ProcedureAction.Paged), Search = normalizedSearch, Page = page, PageSize = pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Team), args, commandType: CommandType.StoredProcedure));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<TeamDto>()).AsList();
        return new PagedResult<TeamDto>(rows, page, pageSize, total);
    }

    public async Task<TeamDto?> GetByIdAsync(long id)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<TeamDto>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Team),
            new { Action = ProcedureNames.Action(ProcedureAction.Fetch), Id = id },
            commandType: CommandType.StoredProcedure));
    }

    public async Task<long> CreateAsync(UpsertTeamRequest request, long createdBy)
    {
        await using var connection = connectionFactory.CreateConnection();
        // A null Active is passed through rather than defaulted here: SP_TEAM INSERT applies
        // ISNULL(@IsActive, 1), so the procedure owns the "new teams are active" default.
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Team),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                Key = request.Key.Trim().ToUpperInvariant(),
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                IsActive = request.Active,
                request.LeadUserId,
                CreatedBy = createdBy
            },
            commandType: CommandType.StoredProcedure));
    }

    public async Task<bool> UpdateAsync(long id, UpsertTeamRequest request)
    {
        await using var connection = connectionFactory.CreateConnection();
        // SP_TEAM UPDATE deliberately ignores @Key: a team key is immutable once issued.
        // A null Active is passed through unchanged: the procedure's ISNULL(@IsActive, IsActive)
        // is what makes "this payload did not mention the flag" leave the stored value alone.
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Team),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Update),
                Id = id,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                IsActive = request.Active,
                request.LeadUserId,
                UserId = currentUser.UserId
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Team),
            new { Action = ProcedureNames.Action(ProcedureAction.Delete), Id = id, UserId = currentUser.UserId },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<bool> AddMemberAsync(long teamId, long userId, string teamRole, long addedBy)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.TeamMember),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                TeamId = teamId,
                UserId = userId,
                TeamRole = teamRole,
                Actor = addedBy
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<bool> RemoveMemberAsync(long teamId, long userId)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.TeamMember),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Delete),
                TeamId = teamId,
                UserId = userId,
                Actor = currentUser.UserId
            },
            commandType: CommandType.StoredProcedure)) > 0;
    }

    public async Task<PagedResult<TeamMemberDto>> ListMembersAsync(long teamId, int page, int pageSize)
    {
        var args = new { Action = ProcedureNames.Action(ProcedureAction.Paged), TeamId = teamId, Page = page, PageSize = pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.TeamMember), args, commandType: CommandType.StoredProcedure));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<TeamMemberDto>()).AsList();
        return new PagedResult<TeamMemberDto>(rows, page, pageSize, total);
    }
}
