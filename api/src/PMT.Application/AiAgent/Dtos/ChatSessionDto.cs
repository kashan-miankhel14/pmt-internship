using PMT.Domain.Enums;

namespace PMT.Application.AiAgent.Dtos;

/// <summary>Summary of a conversation shown in the session list.</summary>
public sealed record ChatSessionDto(
    Guid Id,
    string? Title,
    long? ProjectId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long MessageCount);

/// <summary>A single persisted turn in a conversation.</summary>
public sealed record ChatMessageDto(
    long Id,
    Guid SessionId,
    AiChatRole Role,
    string Content,
    DateTime CreatedAt,
    int? TokenCount,
    int? LatencyMs,
    IReadOnlyCollection<DocumentChunkDto> Citations);

/// <summary>A retrieved knowledge chunk surfaced as a citation.</summary>
public sealed record DocumentChunkDto(
    long Id,
    string EntityType,
    long EntityId,
    long? ProjectId,
    string? Title,
    string Content,
    double Score);

/// <summary>Audit view of one tool invocation performed during a turn.</summary>
public sealed record ToolCallDto(
    long Id,
    Guid SessionId,
    string ToolName,
    string? ArgumentsJson,
    string? ResultJson,
    AiToolStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? DurationMs);
