using System.Data;
using Dapper;
using PMT.Application.AiAgent;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper implementation over the stored procedures defined in <see cref="ProcedureNames"/>.
/// </summary>
public sealed class AiChatRepository(IDbConnectionFactory connectionFactory) : IAiChatRepository
{
    public async Task<Guid> CreateSessionAsync(long userId, string? title, long? projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            ProcedureNames.AiChatSessionCreate,
            new { UserId = userId, Title = Trim(title, 200), ProjectId = projectId, User = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<PagedResult<AiChatSession>> ListSessionsAsync(long userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
            ProcedureNames.AiChatSessionListByUser,
            new { UserId = userId, Page = page, PageSize = pageSize },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var total = await grid.ReadSingleAsync<long>();
        var rows = (await grid.ReadAsync<AiChatSession>()).AsList();
        return new PagedResult<AiChatSession>(rows, page, pageSize, total);
    }

    public async Task<AiChatSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<AiChatSession>(new CommandDefinition(
            ProcedureNames.AiChatSessionGetById,
            new { Id = sessionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, long? deletedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiChatSessionDelete,
            new { Id = sessionId, User = deletedBy },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<long> AddMessageAsync(AiChatMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiChatMessageCreate,
            new
            {
                message.SessionId,
                Role = message.Role.ToString(),
                message.Content,
                message.ContextJson,
                message.TokenCount,
                message.LatencyMs,
                User = message.InsertedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<AiChatMessage>> ListMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AiChatMessage>(new CommandDefinition(
            ProcedureNames.AiChatMessageListBySession,
            new { SessionId = sessionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<long> CreateToolCallAsync(AiAgentToolCall toolCall, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiAgentToolCallCreate,
            new
            {
                toolCall.SessionId,
                toolCall.ToolName,
                toolCall.ArgumentsJson,
                Status = toolCall.Status.ToString(),
                User = toolCall.InsertedBy
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> CompleteToolCallAsync(long id, AiToolStatus status, string? resultJson, long? userId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiAgentToolCallComplete,
            new { Id = id, Status = status.ToString(), ResultJson = resultJson, UserId = userId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyCollection<AiAgentToolCall>> ListToolCallsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<AiAgentToolCall>(new CommandDefinition(
            ProcedureNames.AiAgentToolCallListBySession,
            new { SessionId = sessionId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
