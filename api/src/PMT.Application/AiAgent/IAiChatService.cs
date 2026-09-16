using PMT.Application.AiAgent.Dtos;
using PMT.Application.Common.Models;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Application.AiAgent;

/// <summary>
/// Conversation management for the signed-in user. Every method enforces that the
/// caller owns the session it touches.
/// </summary>
public interface IAiChatService
{
    /// <summary>Lists the current user's conversations.</summary>
    Task<PagedResult<ChatSessionDto>> GetSessionsAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Returns the transcript of a conversation owned by the current user.</summary>
    Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asserts that the conversation exists and belongs to the current user, throwing
    /// NotFoundException otherwise.
    /// </summary>
    /// <remarks>
    /// The ownership guard on its own, for callers that must refuse to act on a session they
    /// cannot prove is the caller's own — the agent's confirm endpoint checks this before it
    /// carries out an approved deletion — without paying for the whole transcript.
    /// </remarks>
    Task<AiChatSession> EnsureOwnedAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty conversation for the current user.</summary>
    Task<Guid> CreateSessionAsync(string? title, long? projectId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a conversation owned by the current user.</summary>
    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an existing session (validating ownership) or creates one seeded with a
    /// title derived from <paramref name="firstMessage"/>.
    /// </summary>
    Task<AiChatSession> ResolveSessionAsync(Guid? sessionId, string firstMessage, long? projectId, CancellationToken cancellationToken = default);

    /// <summary>Appends a message to a session the current user owns.</summary>
    Task<AiChatMessage> AppendMessageAsync(
        Guid sessionId,
        AiChatRole role,
        string content,
        string? contextJson = null,
        int? tokenCount = null,
        int? latencyMs = null,
        CancellationToken cancellationToken = default);
}
