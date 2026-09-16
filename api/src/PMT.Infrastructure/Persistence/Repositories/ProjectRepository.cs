using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Projects;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(IDbConnectionFactory connectionFactory) : IProjectRepository
{
    public async Task<PagedResult<Project>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new { Action = ProcedureNames.Action(ProcedureAction.Paged), Search = normalizedSearch, Page = page, PageSize = pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Project), args, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<Project>()).AsList();
        return new PagedResult<Project>(rows, page, pageSize, total);
    }

    public async Task<Project?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Project>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Project),
            new { Action = ProcedureNames.Action(ProcedureAction.Fetch), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<long> CreateAsync(Project entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Project),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Insert),
                entity.Name,
                entity.Description,
                entity.Key,
                Status = entity.Status.ToString(),
                entity.DepartmentId,
                entity.OwnerUserId,
                entity.StartDate,
                entity.TargetDate,
                entity.Active,
                UserId = entity.InsertedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<long> CreateFromTemplateAsync(string key, string name, string? description, string typeCode, string accessLevel, long leadUserId, long createdBy, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Key", key, DbType.AnsiString, size: 10);
        parameters.Add("Name", name, DbType.String, size: 200);
        parameters.Add("Description", description, DbType.String, size: -1);
        parameters.Add("TypeCode", typeCode, DbType.AnsiString, size: 20);
        parameters.Add("AccessLevel", accessLevel, DbType.AnsiString, size: 20);
        parameters.Add("LeadUserId", leadUserId, DbType.Int64);
        parameters.Add("CreatedBy", createdBy, DbType.Int64);
        parameters.Add("NewProjectId", dbType: DbType.Int64, direction: ParameterDirection.Output);

        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            ProcedureNames.ProjectCreateFromTemplate,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return parameters.Get<long>("NewProjectId");
    }

    public async Task<bool> UpdateAsync(Project entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Project),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Update),
                entity.Id,
                entity.Name,
                entity.Description,
                entity.Key,
                Status = entity.Status.ToString(),
                entity.DepartmentId,
                entity.OwnerUserId,
                entity.StartDate,
                entity.TargetDate,
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
            ProcedureNames.Get(StoredProcedure.Project),
            new { Action = ProcedureNames.Action(ProcedureAction.Delete), Id = id, UserId = deletedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }
}
