using PMT.Domain.Enums;

namespace PMT.Application.Workflow;

/// <summary>One reachable transition as surfaced to the API / agent.</summary>
public sealed record WorkflowTransitionDto(
    long Id,
    string Name,
    string FromStatusCode,
    string FromStatusName,
    string ToStatusCode,
    string ToStatusName,
    WorkflowStatusCategory ToStatusCategory,
    int Order);

/// <summary>A status in the project's workflow catalog.</summary>
public sealed record WorkflowStatusDto(
    long Id,
    string Code,
    string Name,
    WorkflowStatusCategory Category,
    bool IsInitial,
    int Order);

/// <summary>The outcome of running a transition through the engine.</summary>
public sealed record WorkflowResultDto(
    string StatusCode,
    string StatusName,
    WorkflowStatusCategory Category,
    long? WorkflowStatusId,
    IReadOnlyCollection<WorkflowTransitionDto> AvailableTransitions);

/// <summary>A single change-log entry.</summary>
public sealed record IssueHistoryDto(
    long Id,
    string EntityType,
    long EntityId,
    string FieldName,
    string? OldValue,
    string? NewValue,
    string? Comment,
    long? ChangedByUser,
    DateTime ChangedAtUtc);
