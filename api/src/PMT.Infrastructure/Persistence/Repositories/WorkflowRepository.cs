using Dapper;
using PMT.Application.Common.Interfaces;
using PMT.Application.Workflow;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using System.Data;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper-backed <see cref="IWorkflowRepository"/>. Every call dispatches to the SP_WORKFLOW*
/// family, keyed by the <see cref="StoredProcedure"/> registry entries added for the engine.
/// </summary>
public sealed class WorkflowRepository(IDbConnectionFactory connectionFactory) : IWorkflowRepository
{
    public async Task<Workflow?> GetWorkflowByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Workflow>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Workflow),
            new { Action = "FETCH_BY_PROJECT", ProjectId = projectId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<WorkflowStatus>> GetStatusesByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkflowStatus>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.WorkflowStatus),
            new { Action = "FETCH_BY_PROJECT", ProjectId = projectId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyCollection<WorkflowTransition>> GetAvailableTransitionsAsync(long projectId, string fromStatusCode, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<WorkflowTransition>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.WorkflowTransition),
            new { Action = "FETCH_AVAILABLE", ProjectId = projectId, FromStatusCode = string.IsNullOrWhiteSpace(fromStatusCode) ? null : fromStatusCode },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<WorkflowTransition?> GetTransitionByIdAsync(long projectId, long transitionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<WorkflowTransition>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.WorkflowTransition),
            new { Action = "FETCH_BY_ID", ProjectId = projectId, Id = transitionId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<WorkflowTransition?> GetTransitionAsync(long projectId, string fromStatusCode, string toStatusCode, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<WorkflowTransition>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.WorkflowTransition),
            new { Action = "FETCH_ONE", ProjectId = projectId, FromStatusCode = fromStatusCode, ToStatusCode = toStatusCode },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<long> SaveHistoryAsync(IssueHistory entry, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.IssueHistory),
            new
            {
                Action = "INSERT",
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                WorkflowTransitionId = entry.WorkflowTransitionId,
                FieldName = entry.FieldName,
                OldValue = entry.OldValue,
                NewValue = entry.NewValue,
                Comment = entry.Comment,
                ChangedByUser = entry.ChangedByUser,
                UserId = entry.InsertedBy
            },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<IssueHistory>> GetHistoryAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<IssueHistory>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.IssueHistory),
            new { Action = "FETCH", EntityType = entityType, EntityId = entityId },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task StampStatusAsync(string entityType, long entityId, long? workflowStatusId, long? changedByUser, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.WorkflowStatus),
            new
            {
                Action = "STAMP",
                EntityType = entityType,
                EntityId = entityId,
                WorkflowStatusId = workflowStatusId,
                UserId = changedByUser
            },
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));
    }
}
