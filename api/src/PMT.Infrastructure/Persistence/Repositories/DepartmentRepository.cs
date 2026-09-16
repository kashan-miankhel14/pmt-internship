using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Departments;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(IDbConnectionFactory connectionFactory) : IDepartmentRepository
{
    public async Task<PagedResult<Department>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new { Action = ProcedureNames.Action(ProcedureAction.Paged), Search = normalizedSearch, Page = page, PageSize = pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Department), args, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<Department>()).AsList();
        return new PagedResult<Department>(rows, page, pageSize, total);
    }

    public async Task<Department?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Department>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Department), new { Action = ProcedureNames.Action(ProcedureAction.Fetch), Id = id }, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<long> CreateAsync(Department entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Department), new { Action = ProcedureNames.Action(ProcedureAction.Insert), entity.Name, entity.Code, entity.Description, entity.Active, entity.InsertedBy }, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<bool> UpdateAsync(Department entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Department), new { Action = ProcedureNames.Action(ProcedureAction.Update), entity.Id, entity.Name, entity.Code, entity.Description, entity.Active, entity.UpdatedBy }, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Department), new { Action = ProcedureNames.Action(ProcedureAction.Delete), Id = id, UserId = deletedBy }, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken)) > 0;
    }
}
