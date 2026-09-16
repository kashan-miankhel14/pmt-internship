using PMT.Application.AiAgent.Dtos;
using PMT.Application.AiAgent.Models;

namespace PMT.Application.AiAgent;

/// <summary>
/// Source entity discriminators stored in AiDocumentChunk.EntityType.
/// Values must stay within the column's varchar(30) budget.
/// </summary>
public static class AiEntityTypes
{
    public const string Project = "Project";
    public const string UserStory = "UserStory";
    public const string Task = "Task";
    public const string Issue = "Issue";

    public static IReadOnlyCollection<string> All { get; } = [Project, UserStory, Task, Issue];

    public static bool IsKnown(string? entityType) =>
        entityType is not null && All.Contains(entityType, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the canonical casing for a caller-supplied discriminator.</summary>
    public static string Normalize(string entityType) =>
        All.FirstOrDefault(x => string.Equals(x, entityType, StringComparison.OrdinalIgnoreCase)) ?? entityType;
}

/// <summary>
/// Maintains the RAG knowledge store: projects source entities to chunks, embeds
/// them, and retrieves them at query time.
/// </summary>
public interface IAiIndexingService
{
    /// <summary>Indexes or refreshes the chunk for one source entity.</summary>
    Task UpsertAsync(string entityType, long entityId, CancellationToken cancellationToken = default);

    /// <summary>Removes a source entity from the knowledge store.</summary>
    Task RemoveAsync(string entityType, long entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the knowledge store. When <paramref name="force"/> is false, sources whose
    /// chunk is already newer than the source are skipped.
    /// </summary>
    Task<ReindexResult> ReindexAllAsync(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the most relevant chunks for a natural-language query.</summary>
    Task<IReadOnlyCollection<DocumentChunkDto>> SearchAsync(
        string query,
        long? projectId,
        int topN,
        CancellationToken cancellationToken = default);
}
