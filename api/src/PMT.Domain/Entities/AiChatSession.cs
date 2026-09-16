using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

/// <summary>
/// A single AI agent conversation. Mirrors dbo.AiChatSession (migration 0012).
/// </summary>
/// <remarks>
/// This entity intentionally does not derive from <see cref="Common.AuditableEntity"/>:
/// the AI tables use a uniqueidentifier primary key and CreatedAt/UpdatedAt columns
/// rather than the bigint Id + InsertDate/UpdateDate convention used elsewhere.
/// </remarks>
public sealed class AiChatSession
{
    public Guid Id { get; set; }

    /// <summary>Owner of the conversation. Sessions are private to this user.</summary>
    public long UserId { get; set; }

    public string? Title { get; set; }

    /// <summary>Optional project scope hint used to narrow retrieval.</summary>
    public long? ProjectId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public long? InsertedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public long? DeletedBy { get; set; }

    /// <summary>
    /// Projection-only. Populated by usp_AiChatSession_ListByUser; not a stored column.
    /// </summary>
    public long MessageCount { get; set; }
}

/// <summary>
/// One user/assistant/system turn in a conversation. Mirrors dbo.AiChatMessage (migration 0012).
/// </summary>
public sealed class AiChatMessage
{
    public long Id { get; set; }
    public Guid SessionId { get; set; }
    public AiChatRole Role { get; set; } = AiChatRole.User;
    public string Content { get; set; } = string.Empty;

    /// <summary>Serialized retrieval citations / tool summary attached to this turn.</summary>
    public string? ContextJson { get; set; }

    public int? TokenCount { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public long? InsertedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public long? DeletedBy { get; set; }
}
