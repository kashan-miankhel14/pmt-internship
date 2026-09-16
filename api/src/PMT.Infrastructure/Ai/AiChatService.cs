using System.Text.Json;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Dtos;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Models;
using PMT.Domain.Entities;
using PMT.Domain.Enums;
using PMT.Domain.Exceptions;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Conversation management. Sessions are private: every read and write path resolves
/// the session and rejects it unless the current user owns it.
/// </summary>
public sealed class AiChatService(
    IAiChatRepository repository,
    ICurrentUserService currentUser) : IAiChatService
{
    private const int MaxTitleLength = 200;

    public async Task<PagedResult<ChatSessionDto>> GetSessionsAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await repository.ListSessionsAsync(userId, page, pageSize, cancellationToken);
        return new PagedResult<ChatSessionDto>(
            result.Items.Select(MapSession).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await RequireOwnedSessionAsync(sessionId, cancellationToken);
        var messages = await repository.ListMessagesAsync(sessionId, cancellationToken);
        return messages.Select(MapMessage).ToArray();
    }

    public Task<AiChatSession> EnsureOwnedAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        RequireOwnedSessionAsync(sessionId, cancellationToken);

    public async Task<Guid> CreateSessionAsync(string? title, long? projectId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        return await repository.CreateSessionAsync(userId, Normalize(title), projectId, cancellationToken);
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireOwnedSessionAsync(sessionId, cancellationToken);
        if (!await repository.DeleteSessionAsync(session.Id, currentUser.UserId, cancellationToken))
            throw new NotFoundException(nameof(AiChatSession), sessionId);
    }

    public async Task<AiChatSession> ResolveSessionAsync(Guid? sessionId, string firstMessage, long? projectId, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();

        if (sessionId is { } existingId)
            return await RequireOwnedSessionAsync(existingId, cancellationToken);

        var title = BuildTitle(firstMessage);
        var newId = await repository.CreateSessionAsync(userId, title, projectId, cancellationToken);

        // Read back so callers observe the server-assigned timestamps.
        return await repository.GetSessionAsync(newId, cancellationToken)
            ?? throw new AiAgentException("The chat session could not be created.");
    }

    public async Task<AiChatMessage> AppendMessageAsync(
        Guid sessionId,
        AiChatRole role,
        string content,
        string? contextJson = null,
        int? tokenCount = null,
        int? latencyMs = null,
        CancellationToken cancellationToken = default)
    {
        var session = await RequireOwnedSessionAsync(sessionId, cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
            throw new ValidationException(["Message content must not be empty."]);

        var message = new AiChatMessage
        {
            SessionId = session.Id,
            Role = role,
            Content = content,
            ContextJson = contextJson,
            TokenCount = tokenCount,
            LatencyMs = latencyMs,
            InsertedBy = currentUser.UserId
        };

        message.Id = await repository.AddMessageAsync(message, cancellationToken);
        return message;
    }

    /// <summary>
    /// Loads a session and asserts the current user owns it. A session belonging to
    /// someone else is reported as not found so ids cannot be probed.
    /// </summary>
    private async Task<AiChatSession> RequireOwnedSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var session = await repository.GetSessionAsync(sessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(AiChatSession), sessionId);

        if (session.UserId != userId)
            throw new NotFoundException(nameof(AiChatSession), sessionId);

        return session;
    }

    private long RequireUserId() =>
        currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    private static string? Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var trimmed = title.Trim();
        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength];
    }

    /// <summary>Derives a readable session title from the opening message.</summary>
    private static string? BuildTitle(string firstMessage)
    {
        if (string.IsNullOrWhiteSpace(firstMessage)) return null;

        var collapsed = string.Join(' ', firstMessage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= 60) return collapsed;

        // Prefer a word boundary so titles do not end mid-token.
        var cut = collapsed.LastIndexOf(' ', 59);
        return (cut > 20 ? collapsed[..cut] : collapsed[..60]) + "...";
    }

    internal static ChatSessionDto MapSession(AiChatSession x) =>
        new(x.Id, x.Title, x.ProjectId, x.CreatedAt, x.UpdatedAt, x.MessageCount);

    internal static ChatMessageDto MapMessage(AiChatMessage x) =>
        new(x.Id, x.SessionId, x.Role, x.Content, x.CreatedAt, x.TokenCount, x.LatencyMs, ParseCitations(x.ContextJson));

    /// <summary>
    /// Citations are stored as opaque JSON. A malformed or legacy payload must not break
    /// transcript reads, so parse failures degrade to an empty citation list.
    /// </summary>
    private static IReadOnlyCollection<DocumentChunkDto> ParseCitations(string? contextJson)
    {
        if (string.IsNullOrWhiteSpace(contextJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<DocumentChunkDto[]>(contextJson, JsonDefaults.Options)
                ?? Array.Empty<DocumentChunkDto>();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>Shared JSON settings for AI payloads persisted in ContextJson / ResultJson.</summary>
internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
