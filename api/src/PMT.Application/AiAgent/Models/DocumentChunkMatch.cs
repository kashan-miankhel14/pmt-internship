namespace PMT.Application.AiAgent.Models;

/// <summary>
/// A row returned by usp_AiDocumentChunk_Search. Kept separate from the
/// <see cref="Domain.Entities.AiDocumentChunk"/> entity because the similarity
/// score is a per-query artifact, not stored state.
/// </summary>
/// <remarks>
/// Mutable properties with names matching the procedure's column list so Dapper
/// can materialize it without a custom mapper.
/// </remarks>
public sealed class DocumentChunkMatch
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public long? ProjectId { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SearchText { get; set; }
    public DateTime IndexedAt { get; set; }

    /// <summary>Cosine similarity in [-1, 1]; 0 when no embedding comparison was possible.</summary>
    public double Similarity { get; set; }

    /// <summary>1 when the keyword LIKE filter matched. Kept as int to match the procedure's projection.</summary>
    public int TextMatch { get; set; }

    public bool IsTextMatch => TextMatch != 0;
}

/// <summary>
/// A row returned by usp_AiDocumentChunk_SourceSnapshot, used by the indexer to
/// diff stored chunks against live source data.
/// </summary>
public sealed class DocumentChunkSnapshot
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public long? ProjectId { get; set; }
    public DateTime SourceUpdatedAt { get; set; }
    public DateTime IndexedAt { get; set; }
}

/// <summary>Outcome of a reindex run.</summary>
/// <param name="Indexed">Chunks inserted or refreshed.</param>
/// <param name="Skipped">Sources already up to date.</param>
/// <param name="Failed">Sources that could not be embedded or persisted.</param>
/// <param name="Duration">Wall-clock time of the run.</param>
public sealed record ReindexResult(int Indexed, int Skipped, int Failed, TimeSpan Duration);
