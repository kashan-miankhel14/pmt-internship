using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

/// <summary>
/// Audit record for a single tool invocation made by the agent.
/// Mirrors dbo.AiAgentToolCall (migration 0013).
/// </summary>
public sealed class AiAgentToolCall
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Arguments the model supplied, as raw JSON.</summary>
    public string? ArgumentsJson { get; set; }

    /// <summary>Tool output (or the error envelope on failure), as raw JSON.</summary>
    public string? ResultJson { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public AiToolStatus Status { get; set; } = AiToolStatus.Running;

    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public long? InsertedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public long? DeletedBy { get; set; }
}
