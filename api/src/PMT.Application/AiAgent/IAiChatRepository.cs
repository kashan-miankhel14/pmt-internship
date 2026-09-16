using PMT.Application.Common.Models;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent;

/// <summary>
/// Persistence for chat sessions, messages and the tool-call audit trail.
/// Backed by the usp_AiChat* / usp_AiAgentToolCall_* procedures (migrations 0012-0013).
/// </summary>
public interface IAiChatRepository
{
    /// <summary>Creates a conversation and returns its server-generated id.</summary>
    Task<Guid> CreateSessionAsync(long userId, string? title, long? projectId, CancellationToken cancellationToken = default);

    /// <summary>Lists a user's non-deleted sessions, most recently active first.</summary>
    Task<PagedResult<AiChatSession>> ListSessionsAsync(long userId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single session regardless of owner. Callers are responsible for the
    /// ownership check; use this to enforce it rather than to bypass it.
    /// </summary>
    Task<AiChatSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a session and its messages.</summary>
    Task<bool> DeleteSessionAsync(Guid sessionId, long? deletedBy, CancellationToken cancellationToken = default);

    /// <summary>Appends a message and bumps the session's UpdatedAt.</summary>
    Task<long> AddMessageAsync(AiChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>Returns the full ordered transcript for a session.</summary>
    Task<IReadOnlyCollection<AiChatMessage>> ListMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Records the start of a tool invocation and returns its id.</summary>
    Task<long> CreateToolCallAsync(AiAgentToolCall toolCall, CancellationToken cancellationToken = default);

    /// <summary>Finalizes a tool invocation with its terminal status and result payload.</summary>
    Task<bool> CompleteToolCallAsync(long id, AiToolStatus status, string? resultJson, long? userId, CancellationToken cancellationToken = default);

    /// <summary>Returns the ordered tool-call audit trail for a session.</summary>
    Task<IReadOnlyCollection<AiAgentToolCall>> ListToolCallsAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
