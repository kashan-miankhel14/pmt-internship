using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Dtos;
using PMT.Application.AiAgent.Models;
using PMT.Application.Common.Interfaces;
using PMT.Application.Issues;
using PMT.Application.Projects;
using PMT.Application.Tasks;
using PMT.Application.UserStories;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Projects PMT entities into retrievable chunks, embeds them with the configured
/// embedding model, and serves similarity queries.
/// </summary>
public sealed class AiIndexingService(
    IAiIndexRepository indexRepository,
    IAiEmbeddingClient embeddingClient,
    IProjectRepository projectRepository,
    IUserStoryRepository storyRepository,
    ITaskRepository taskRepository,
    IIssueRepository issueRepository,
    ICurrentUserService currentUser,
    ILogger<AiIndexingService> logger) : IAiIndexingService
{
    /// <summary>Page size used when walking source tables during a full reindex.</summary>
    private const int SourcePageSize = 200;

    /// <summary>Embedding inputs are capped so a huge description cannot blow up the model call.</summary>
    private const int MaxEmbeddingChars = 6000;

    public async Task UpsertAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(entityType);
        var chunk = await BuildChunkAsync(normalized, entityId, cancellationToken);

        if (chunk is null)
        {
            // The source is gone or soft-deleted; drop any stale chunk so retrieval
            // cannot surface deleted work.
            await indexRepository.DeleteChunkAsync(normalized, entityId, currentUser.UserId, cancellationToken);
            return;
        }

        await EmbedAsync(chunk, cancellationToken);
        await indexRepository.UpsertChunkAsync(chunk, cancellationToken);
    }

    public async Task RemoveAsync(string entityType, long entityId, CancellationToken cancellationToken = default)
        => await indexRepository.DeleteChunkAsync(Normalize(entityType), entityId, currentUser.UserId, cancellationToken);

    public async Task<ReindexResult> ReindexAllAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var indexed = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var entityType in AiEntityTypes.All)
        {
            // One snapshot per type keeps the freshness check to a single round trip
            // instead of one per source row.
            var snapshot = (await indexRepository.GetSourceSnapshotAsync(entityType, null, null, cancellationToken))
                .ToDictionary(x => x.EntityId, x => x.IndexedAt);

            await foreach (var chunk in EnumerateSourceChunksAsync(entityType, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!force
                    && snapshot.TryGetValue(chunk.EntityId, out var indexedAt)
                    && indexedAt >= chunk.SourceUpdatedAt)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    await EmbedAsync(chunk, cancellationToken);
                    await indexRepository.UpsertChunkAsync(chunk, cancellationToken);
                    indexed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A single bad row must not abort a long reindex.
                    failed++;
                    logger.LogWarning(ex, "Failed to index {EntityType} {EntityId}.", chunk.EntityType, chunk.EntityId);
                }
            }
        }

        stopwatch.Stop();
        logger.LogInformation(
            "AI reindex finished: {Indexed} indexed, {Skipped} skipped, {Failed} failed in {Elapsed}.",
            indexed, skipped, failed, stopwatch.Elapsed);

        return new ReindexResult(indexed, skipped, failed, stopwatch.Elapsed);
    }

    public async Task<IReadOnlyCollection<DocumentChunkDto>> SearchAsync(
        string query,
        long? projectId,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        byte[]? embedding = null;
        try
        {
            embedding = EmbeddingSerializer.Serialize(await embeddingClient.GenerateEmbeddingAsync(query, cancellationToken));
        }
        catch (AiServiceUnavailableException ex)
        {
            // Degrade to keyword-only retrieval rather than failing the whole turn.
            logger.LogWarning(ex, "Embedding unavailable; falling back to keyword retrieval.");
        }

        var keywords = NormalizeSearchText(query);

        // With neither a vector nor usable keywords there is nothing to match on, and the
        // stored procedure would raise. Return empty so the caller degrades quietly.
        if (embedding is null && string.IsNullOrWhiteSpace(keywords)) return [];

        var matches = await indexRepository.SearchAsync(
            embedding,
            keywords,
            entityType: null,
            projectId: projectId,
            topN: topN,
            minScore: null,
            cancellationToken: cancellationToken);

        return matches.Select(x => new DocumentChunkDto(
            x.Id, x.EntityType, x.EntityId, x.ProjectId, x.Title, x.Content, Math.Round(x.Similarity, 4))).ToArray();
    }

    // ------------------------------------------------------------------
    // Chunk construction
    // ------------------------------------------------------------------

    private async Task EmbedAsync(AiDocumentChunk chunk, CancellationToken cancellationToken)
    {
        var input = chunk.Content.Length > MaxEmbeddingChars ? chunk.Content[..MaxEmbeddingChars] : chunk.Content;
        chunk.Embedding = EmbeddingSerializer.Serialize(await embeddingClient.GenerateEmbeddingAsync(input, cancellationToken));
    }

    private async Task<AiDocumentChunk?> BuildChunkAsync(string entityType, long entityId, CancellationToken cancellationToken)
        => entityType switch
        {
            AiEntityTypes.Project => ProjectChunk(await projectRepository.GetByIdAsync(entityId, cancellationToken)),
            AiEntityTypes.UserStory => StoryChunk(await storyRepository.GetByIdAsync(entityId, cancellationToken)),
            AiEntityTypes.Task => TaskChunk(await taskRepository.GetByIdAsync(entityId, cancellationToken)),
            AiEntityTypes.Issue => IssueChunk(await issueRepository.GetByIdAsync(entityId, cancellationToken)),
            _ => throw new ValidationException([$"'{entityType}' is not an indexable entity type."])
        };

    private async IAsyncEnumerable<AiDocumentChunk> EnumerateSourceChunksAsync(
        string entityType,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunks = new List<AiDocumentChunk>();
            var hasMore = false;

            switch (entityType)
            {
                case AiEntityTypes.Project:
                {
                    var result = await projectRepository.GetPagedAsync(page, SourcePageSize, null, cancellationToken);
                    chunks.AddRange(result.Items.Select(ProjectChunk).OfType<AiDocumentChunk>());
                    hasMore = HasMore(result.Items.Count, page, result.TotalCount);
                    break;
                }
                case AiEntityTypes.UserStory:
                {
                    var result = await storyRepository.GetPagedAsync(page, SourcePageSize, null, cancellationToken);
                    chunks.AddRange(result.Items.Select(StoryChunk).OfType<AiDocumentChunk>());
                    hasMore = HasMore(result.Items.Count, page, result.TotalCount);
                    break;
                }
                case AiEntityTypes.Task:
                {
                    var result = await taskRepository.GetPagedAsync(page, SourcePageSize, null, cancellationToken);
                    chunks.AddRange(result.Items.Select(TaskChunk).OfType<AiDocumentChunk>());
                    hasMore = HasMore(result.Items.Count, page, result.TotalCount);
                    break;
                }
                case AiEntityTypes.Issue:
                {
                    var result = await issueRepository.GetPagedAsync(page, SourcePageSize, null, cancellationToken);
                    chunks.AddRange(result.Items.Select(IssueChunk).OfType<AiDocumentChunk>());
                    hasMore = HasMore(result.Items.Count, page, result.TotalCount);
                    break;
                }
                default:
                    yield break;
            }

            foreach (var chunk in chunks)
                yield return chunk;

            if (!hasMore) yield break;
            page++;
        }
    }

    private static bool HasMore(int received, int page, long total) =>
        received > 0 && (long)page * SourcePageSize < total;

    private AiDocumentChunk? ProjectChunk(Project? entity)
    {
        if (entity is null || entity.IsDeleted) return null;

        var content = Compose(
            ("Project", entity.Name),
            ("Key", entity.Key),
            ("Status", entity.Status.ToString()),
            ("Description", entity.Description),
            ("Start date", Format(entity.StartDate)),
            ("Target date", Format(entity.TargetDate)),
            ("Owner user id", Format(entity.OwnerUserId)),
            ("Department id", Format(entity.DepartmentId)));

        return Chunk(AiEntityTypes.Project, entity.Id, entity.Id, $"{entity.Key} - {entity.Name}", content, entity);
    }

    private AiDocumentChunk? StoryChunk(UserStory? entity)
    {
        if (entity is null || entity.IsDeleted) return null;

        var content = Compose(
            ("Story", entity.Title),
            ("Status", entity.Status.ToString()),
            ("Priority", Format(entity.Priority)),
            ("Story points", Format(entity.StoryPoints)),
            ("Assignee user id", Format(entity.AssigneeUserId)),
            ("Description", entity.Description),
            ("Acceptance criteria", entity.AcceptanceCriteria));

        return Chunk(AiEntityTypes.UserStory, entity.Id, entity.ProjectId, entity.Title, content, entity);
    }

    private AiDocumentChunk? TaskChunk(TaskItem? entity)
    {
        if (entity is null || entity.IsDeleted) return null;

        var content = Compose(
            ("Task", entity.Title),
            ("Status", entity.Status.ToString()),
            ("Priority", Format(entity.Priority)),
            ("Story id", Format(entity.StoryId)),
            ("Assignee user id", Format(entity.AssigneeUserId)),
            ("Estimate hours", Format(entity.EstimateHours)),
            ("Actual hours", Format(entity.ActualHours)),
            ("Due date", Format(entity.DueDate)),
            ("Description", entity.Description));

        return Chunk(AiEntityTypes.Task, entity.Id, entity.ProjectId, entity.Title, content, entity);
    }

    private AiDocumentChunk? IssueChunk(Issue? entity)
    {
        if (entity is null || entity.IsDeleted) return null;

        var content = Compose(
            ("Issue", entity.Title),
            ("Status", entity.Status.ToString()),
            ("Severity", entity.Severity.ToString()),
            ("Task id", Format(entity.TaskId)),
            ("Reported by user id", Format(entity.ReportedByUserId)),
            ("Assignee user id", Format(entity.AssignedToUserId)),
            ("Resolved date", Format(entity.ResolvedDate)),
            ("Description", entity.Description));

        return Chunk(AiEntityTypes.Issue, entity.Id, entity.ProjectId, entity.Title, content, entity);
    }

    private AiDocumentChunk Chunk(
        string entityType,
        long entityId,
        long? projectId,
        string? title,
        string content,
        Domain.Common.AuditableEntity source) => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ProjectId = projectId,
            Title = title,
            Content = content,
            SearchText = NormalizeSearchText($"{title} {content}"),
            SourceUpdatedAt = source.UpdateDate ?? source.InsertDate,
            IndexedAt = DateTime.UtcNow,
            InsertedBy = currentUser.UserId
        };

    /// <summary>Joins populated label/value pairs into the text that gets embedded.</summary>
    private static string Compose(params (string Label, string? Value)[] fields)
    {
        var builder = new StringBuilder();
        foreach (var (label, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            builder.Append(label).Append(": ").Append(value.Trim()).Append('\n');
        }
        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Lower-cases and strips punctuation for the SearchText LIKE fallback, and clamps to
    /// the column's 4000-character budget.
    /// </summary>
    private static string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        var normalized = builder.ToString().Trim();
        return normalized.Length <= 4000 ? normalized : normalized[..4000];
    }

    private static string Normalize(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ValidationException(["Entity type is required."]);
        if (!AiEntityTypes.IsKnown(entityType))
            throw new ValidationException([$"'{entityType}' is not an indexable entity type."]);
        return AiEntityTypes.Normalize(entityType);
    }

    private static string? Format(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Format(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string? Format(long? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
