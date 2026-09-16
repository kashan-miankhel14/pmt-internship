using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Application.Tasks;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class TaskRepository(IDbConnectionFactory connectionFactory) : ITaskRepository
{
    public async Task<PagedResult<TaskItem>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var args=new {Action=ProcedureNames.Action(ProcedureAction.Paged),Search=normalizedSearch,Page=page,PageSize=pageSize};
        await using var connection = connectionFactory.CreateConnection();
        using var grid=await connection.QueryMultipleAsync(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Task),args,commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken)); var total=await grid.ReadSingleAsync<long>(); var rows=(await grid.ReadAsync<TaskItem>()).AsList();
        return new PagedResult<TaskItem>(rows, page, pageSize, total);
    }

    public async Task<TaskItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<TaskItem>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Task),new {Action=ProcedureNames.Action(ProcedureAction.Fetch),Id=id},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken));
    }

    public async Task<long> CreateAsync(TaskItem entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Task),new {Action=ProcedureNames.Action(ProcedureAction.Insert),entity.StoryId,entity.AssigneeUserId,entity.Title,entity.Description,entity.EstimateHours,entity.ActualHours, Priority = entity.Priority, ProjectId = entity.ProjectId, Status =entity.Status.ToString(),entity.DueDate,entity.Active,entity.TeamId,UserId=entity.InsertedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken));
    }

    public async Task<bool> UpdateAsync(TaskItem entity, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Task),new {Action=ProcedureNames.Action(ProcedureAction.Update),entity.Id,entity.StoryId,entity.AssigneeUserId,entity.Title,entity.Description,entity.EstimateHours,entity.ActualHours,Priority=entity.Priority,ProjectId=entity.ProjectId, Status=entity.Status.ToString(),entity.DueDate,CompletedDate=entity.CompletedDate,entity.Active,entity.TeamId,UserId=entity.UpdatedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0;
    }

    public async Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(ProcedureNames.Get(StoredProcedure.Task),new {Action=ProcedureNames.Action(ProcedureAction.Delete),Id=id,UserId=deletedBy},commandType:CommandType.StoredProcedure,cancellationToken:cancellationToken))>0;
    }

    public async Task<bool> AreChildTasksResolvedAsync(long storyId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var open = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Task),
            new { Action = ProcedureNames.Action(ProcedureAction.CheckChildren), StoryId = storyId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return (open ?? 0) == 0;
    }
}
