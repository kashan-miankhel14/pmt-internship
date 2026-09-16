using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Sprints;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper-backed <see cref="ISprintRepository"/>. Every call is a single stored procedure
/// invocation against SP_SPRINT, dispatched by the @Action argument, except
/// <see cref="StartAsync"/> and <see cref="CompleteAsync"/> which use the dedicated
/// transition procedures from <see cref="ProcedureNames"/>.
/// </summary>
public sealed class SprintRepository(IDbConnectionFactory connectionFactory) : ISprintRepository
{
    private static string ToDbStatus(SprintStatus status) => status.ToString().ToUpperInvariant();

    public async Task<PagedResult<Sprint>> GetPagedAsync(long projectId, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
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
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Sprint), args, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<Sprint>()).AsList();
        return new PagedResult<Sprint>(rows, page, pageSize, total);
    }

    public async Task<Sprint?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Sprint>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Sprint),
            new { Action = ProcedureNames.Action(ProcedureAction.Fetch), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<long> CreateAsync(Sprint entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Sprint),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                entity.ProjectId,
                entity.Name,
                entity.Goal,
                entity.StartDate,
                entity.EndDate,
                Status = ToDbStatus(entity.Status),
                entity.Active,
                UserId = entity.InsertedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Sprint entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Sprint),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Update),
                entity.Id,
                entity.ProjectId,
                entity.Name,
                entity.Goal,
                entity.StartDate,
                entity.EndDate,
                Status = ToDbStatus(entity.Status),
                entity.Active,
                UserId = entity.UpdatedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Sprint),
            new { Action = ProcedureNames.Action(ProcedureAction.Delete), Id = id, UserId = deletedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<long> StartAsync(long id, long? startedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.SprintStart,
            new { Id = id, UserId = startedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<long> CompleteAsync(long id, long? completedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.SprintComplete,
            new { Id = id, UserId = completedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }
}
