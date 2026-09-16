using System.Data;
using Dapper;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Models;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>
/// Dapper implementation over the stored procedures defined in <see cref="ProcedureNames"/>.
/// </summary>
public sealed class AiIndexRepository(IDbConnectionFactory connectionFactory) : IAiIndexRepository
{
    public async Task<long> UpsertChunkAsync(AiDocumentChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var parameters = new DynamicParameters();
        parameters.Add("EntityType", chunk.EntityType, DbType.AnsiString, size: 30);
        parameters.Add("EntityId", ToEntityId(chunk.EntityId), DbType.Int32);
        parameters.Add("ProjectId", chunk.ProjectId, DbType.Int64);
        parameters.Add("Title", Trim(chunk.Title, 300), DbType.String, size: 300);
        parameters.Add("Content", chunk.Content, DbType.String, size: -1);
        parameters.Add("SearchText", Trim(chunk.SearchText, 4000), DbType.String, size: 4000);
        // Explicit size -1 keeps the driver on varbinary(max) instead of inferring a
        // fixed width from the current vector length.
        parameters.Add("Embedding", chunk.Embedding, DbType.Binary, size: -1);
        parameters.Add("SourceUpdatedAt", chunk.SourceUpdatedAt, DbType.DateTime2);
        parameters.Add("User", chunk.InsertedBy ?? chunk.UpdatedBy, DbType.Int64);

        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiDocumentChunkUpsert,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteChunkAsync(string entityType, long entityId, long? userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", null, DbType.Int64);
        parameters.Add("EntityType", entityType, DbType.AnsiString, size: 30);
        parameters.Add("EntityId", ToEntityId(entityId), DbType.Int32);
        parameters.Add("User", userId, DbType.Int64);

        await using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ProcedureNames.AiDocumentChunkDelete,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken)) > 0;
    }

    public async Task<IReadOnlyCollection<DocumentChunkMatch>> SearchAsync(
        byte[]? queryEmbedding,
        string? queryText,
        string? entityType,
        long? projectId,
        int topN,
        double? minScore,
        CancellationToken cancellationToken = default)
    {
        var normalizedText = string.IsNullOrWhiteSpace(queryText) ? null : Trim(queryText.Trim(), 4000);

        // The procedure rejects a call with neither an embedding nor text; fail fast with
        // a clearer message than a raw SQL THROW.
        if (queryEmbedding is not { Length: > 0 } && normalizedText is null)
            throw new AiAgentException("A retrieval query requires either an embedding or query text.");

        var parameters = new DynamicParameters();
        parameters.Add("QueryEmbedding", queryEmbedding, DbType.Binary, size: -1);
        parameters.Add("QueryText", normalizedText, DbType.String, size: 4000);
        parameters.Add("EntityType", entityType, DbType.AnsiString, size: 30);
        parameters.Add("ProjectId", projectId, DbType.Int64);
        parameters.Add("TopN", Math.Clamp(topN <= 0 ? 5 : topN, 1, 100), DbType.Int32);
        parameters.Add("MinScore", minScore, DbType.Double);

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DocumentChunkMatch>(new CommandDefinition(
            ProcedureNames.AiDocumentChunkSearch,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyCollection<DocumentChunkSnapshot>> GetSourceSnapshotAsync(
        string entityType,
        long? entityId,
        long? projectId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("EntityType", entityType, DbType.AnsiString, size: 30);
        parameters.Add("EntityId", entityId.HasValue ? (int?)ToEntityId(entityId.Value) : null, DbType.Int32);
        parameters.Add("ProjectId", projectId, DbType.Int64);

        await using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<DocumentChunkSnapshot>(new CommandDefinition(
            ProcedureNames.AiDocumentChunkSourceSnapshot,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>
    /// AiDocumentChunk.EntityId is int while PMT entity keys are bigint. Range-check the
    /// narrowing rather than letting SQL Server raise an opaque conversion error.
    /// </summary>
    private static int ToEntityId(long entityId) =>
        entityId is > 0 and <= int.MaxValue
            ? (int)entityId
            : throw new AiAgentException(
                $"Entity id {entityId} cannot be indexed: AiDocumentChunk.EntityId is a 32-bit column.");

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
