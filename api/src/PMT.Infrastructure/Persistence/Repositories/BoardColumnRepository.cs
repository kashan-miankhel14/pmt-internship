using Dapper;
using PMT.Application.Boards;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper-backed <see cref="IBoardColumnRepository"/>. Every call is a single stored
/// procedure invocation against SP_BOARD_COLUMN, dispatched by the @Action argument.
/// </summary>
public sealed class BoardColumnRepository(IDbConnectionFactory connectionFactory) : IBoardColumnRepository
{
    public async Task<IReadOnlyCollection<BoardColumn>> GetByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        // The procedure already orders by Ordinal, so the sequence returned here is the
        // left-to-right board order.
        return (await connection.QueryAsync<BoardColumn>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.BoardColumn),
            new { Action = ProcedureNames.Action(ProcedureAction.Fetch), ProjectId = projectId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken))).AsList();
    }

    public async Task<long> CreateAsync(BoardColumn entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.BoardColumn),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                entity.ProjectId,
                entity.Name,
                entity.Ordinal,
                entity.CompleteColumn,
                entity.CreatedBy,
                UserId = entity.InsertedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(BoardColumn entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.BoardColumn),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Update),
                entity.Id,
                entity.ProjectId,
                entity.Name,
                entity.Ordinal,
                entity.CompleteColumn,
                UserId = entity.UpdatedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.BoardColumn),
            new { Action = ProcedureNames.Action(ProcedureAction.Delete), Id = id, UserId = deletedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }
}
