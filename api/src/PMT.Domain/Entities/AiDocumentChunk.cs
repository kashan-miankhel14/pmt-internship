namespace PMT.Domain.Entities;

/// <summary>
/// One indexed chunk of a source entity, used for retrieval-augmented generation.
/// Mirrors dbo.AiDocumentChunk (migration 0013).
/// </summary>
public sealed class AiDocumentChunk
{
    public long Id { get; set; }

    /// <summary>Source entity discriminator, e.g. Project / UserStory / Task / Issue.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Source entity key. The column is int in SQL while PMT entity keys are bigint,
    /// so values are range-checked before they are sent to the database.
    /// </summary>
    public long EntityId { get; set; }

    public long? ProjectId { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Normalized lower-case text used for the LIKE keyword fallback.</summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// Big-endian IEEE-754 float32 vector. See EmbeddingSerializer for why the byte
    /// order is big-endian rather than the little-endian noted in the migration header.
    /// </summary>
    public byte[]? Embedding { get; set; }

    public DateTime SourceUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;

    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public long? InsertedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public DateTime? DeletedDate { get; set; }
    public long? DeletedBy { get; set; }
}
