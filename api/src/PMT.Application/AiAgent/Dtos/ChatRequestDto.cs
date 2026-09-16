namespace PMT.Application.AiAgent.Dtos;

/// <summary>
/// Inbound payload for POST /api/v1/ai/agent/chat.
/// </summary>
/// <param name="SessionId">Existing conversation to continue. When null a new session is created.</param>
/// <param name="Message">The user's natural-language message.</param>
/// <param name="ProjectId">Optional project scope hint that narrows retrieval and tool results.</param>
public sealed record ChatRequestDto(
    Guid? SessionId,
    string Message,
    long? ProjectId);

/// <summary>
/// Result of a completed agent turn.
/// </summary>
/// <param name="SessionId">The conversation the turn belongs to (created on demand).</param>
/// <param name="Message">The persisted assistant reply.</param>
/// <param name="ToolCalls">Tool invocations the agent performed while producing the reply.</param>
/// <param name="Citations">Knowledge chunks used to ground the reply.</param>
/// <param name="Steps">Number of reasoning steps consumed.</param>
/// <param name="Truncated">True when the agent hit the step budget before finishing.</param>
/// <param name="RequiresConfirmation">
/// True when the turn stopped short of a destructive action and is waiting for the user to
/// approve it. Nothing was deleted when this is set.
/// </param>
/// <param name="PendingActions">The destructive actions awaiting approval, described in plain language.</param>
/// <param name="ConfirmationToken">
/// One-time token to post back to /api/v1/ai/agent/confirm together with "approve" or "cancel".
/// </param>
/// <remarks>
/// The confirmation members are optional with defaults so every existing caller keeps compiling
/// and a turn that needed no confirmation serialises exactly as it did before.
/// </remarks>
public sealed record ChatResponseDto(
    Guid SessionId,
    ChatMessageDto Message,
    IReadOnlyCollection<ToolCallDto> ToolCalls,
    IReadOnlyCollection<DocumentChunkDto> Citations,
    int Steps,
    bool Truncated,
    bool RequiresConfirmation = false,
    IReadOnlyCollection<AgentPendingActionDto>? PendingActions = null,
    string? ConfirmationToken = null);
