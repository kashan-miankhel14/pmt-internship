using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.UserStories;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class UserStoryRepository(IDbConnectionFactory connectionFactory) : IUserStoryRepository
{
    public async Task<PagedResult<UserStory>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args = new { Action=ProcedureNames.Action(ProcedureAction.Paged), Search=normalizedSearch, Page=page, PageSize=pageSize };
        await using var connection = connectionFactory.CreateConnection();
        using var grid=await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.UserStory),args,commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken)); var total=await grid.ReadSingleAsync<long>(); var rows=(await grid.ReadAsync<UserStory>()).AsList();
        return new PagedResult<UserStory>(rows, page, pageSize, total);
    }

    public async Task<UserStory?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<UserStory>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.UserStory),new {Action=ProcedureNames.Action(ProcedureAction.Fetch),Id=id},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken));
    }

    public async Task<long> CreateAsync(UserStory entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.UserStory),new {Action=ProcedureNames.Action(ProcedureAction.Insert),entity.ProjectId,entity.Title,entity.Description,entity.AcceptanceCriteria, Priority = entity.Priority, StoryPoints = entity.StoryPoints, AssigneeUserId = entity.AssigneeUserId, SprintId = entity.SprintId, Status =entity.Status.ToString(),entity.Active,entity.TeamId,UserId=entity.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken));
    }

    public async Task<bool> UpdateAsync(UserStory entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.UserStory),new {Action=ProcedureNames.Action(ProcedureAction.Update),entity.Id,entity.ProjectId,entity.Title,entity.Description,entity.AcceptanceCriteria, Priority=entity.Priority, StoryPoints= entity.StoryPoints, AssigneeUserId= entity.AssigneeUserId, SprintId = entity.SprintId, Status =entity.Status.ToString(),entity.Active,entity.TeamId,UserId=entity.UpdatedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0;
    }

    public async Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.UserStory),new {Action=ProcedureNames.Action(ProcedureAction.Delete),Id=id,UserId=deletedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0;
    }
}
