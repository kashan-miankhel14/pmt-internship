using PMT.Application.AiAgent.Models;

namespace PMT.Application.AiAgent;

/// <summary>
/// Tool-calling chat completions. Implemented once per supported backend (Ollama locally,
/// Gemini in the cloud) and selected by configuration, so the agent loop is never bound to a
/// specific LLM provider.
/// </summary>
public interface IAiChatCompletionClient
{
    /// <summary>
    /// Runs one completion. Implementations must throw
    /// <see cref="Domain.Exceptions.AiServiceUnavailableException"/> when the backend
    /// is unreachable or returns an unusable payload.
    /// </summary>
    Task<AiCompletionResult> ChatWithToolsAsync(
        IReadOnlyList<AiCompletionMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Text embedding generation used by the RAG indexer and retriever.
/// </summary>
public interface IAiEmbeddingClient
{
    /// <summary>Returns the embedding vector for <paramref name="text"/>.</summary>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
