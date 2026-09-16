using PMT.Application.AiAgent.Models;
using PMT.Domain.Entities;

namespace PMT.Application.AiAgent;

/// <summary>
/// Persistence for the RAG knowledge store. Backed by the usp_AiDocumentChunk_*
/// procedures (migration 0013).
/// </summary>
public interface IAiIndexRepository
{
    /// <summary>
    /// Inserts or refreshes the chunk for a source entity. The database enforces one
    /// live chunk per (EntityType, EntityId) and reactivates soft-deleted rows.
    /// </summary>
    Task<long> UpsertChunkAsync(AiDocumentChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes the chunk for a source entity.</summary>
    Task<bool> DeleteChunkAsync(string entityType, long entityId, long? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hybrid retrieval: cosine similarity over <paramref name="queryEmbedding"/> combined
    /// with an optional keyword filter. At least one of embedding or text must be supplied.
    /// </summary>
    Task<IReadOnlyCollection<DocumentChunkMatch>> SearchAsync(
        byte[]? queryEmbedding,
        string? queryText,
        string? entityType,
        long? projectId,
        int topN,
        double? minScore,
        CancellationToken cancellationToken = default);

    /// <summary>Returns stored chunk timestamps so the indexer can skip unchanged sources.</summary>
    Task<IReadOnlyCollection<DocumentChunkSnapshot>> GetSourceSnapshotAsync(
        string entityType,
        long? entityId,
        long? projectId,
        CancellationToken cancellationToken = default);
}
