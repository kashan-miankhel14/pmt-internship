using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Models;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Exceptions;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Ollama-backed implementation of the assistant helpers, tool-calling chat
/// completions and embedding generation.
/// </summary>
public sealed class OllamaClient(
    HttpClient client,
    IOptions<OllamaOptions> options,
    ILogger<OllamaClient> logger)
    : IAiAssistant, IAiChatCompletionClient, IAiEmbeddingClient
{
    private static readonly JsonSerializerOptions ResponseSerializerOptions = new(JsonSerializerDefaults.Web);

    // ---------------------------------------------------------------------
    // IAiAssistant (existing behaviour, unchanged)
    // ---------------------------------------------------------------------

    public Task<string> BreakDownStoryAsync(string title, string? description, CancellationToken cancellationToken = default)
        => GenerateAsync($"Break this user story into implementable tasks.\nTitle: {title}\nDescription: {description}", cancellationToken);

    public Task<string> SummarizeThreadAsync(IEnumerable<string> comments, CancellationToken cancellationToken = default)
        => GenerateAsync("Summarize this project discussion, decisions, blockers, and actions:\n" + string.Join("\n", comments), cancellationToken);

    private async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/generate", new { model = options.Value.Model, prompt, stream = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken: cancellationToken);
        stopwatch.Stop();
        LogLatency("generate", options.Value.Model, stopwatch.ElapsedMilliseconds);
        return payload?.Response ?? string.Empty;
    }

    private sealed record OllamaResponse([property: JsonPropertyName("response")] string Response);

    // ---------------------------------------------------------------------
    // IAiChatCompletionClient
    // ---------------------------------------------------------------------

    /// <summary>
    /// Calls POST /api/chat with the OpenAI-compatible tool schema and returns either
    /// the assistant's text or the tool calls it wants executed.
    /// </summary>
    public async Task<AiCompletionResult> ChatWithToolsAsync(
        IReadOnlyList<AiCompletionMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        var payload = BuildChatRequest(messages, tools);
        var stopwatch = Stopwatch.StartNew();

        JsonElement root;
        try
        {
            using var response = await client.PostAsJsonAsync("/api/chat", payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Ollama chat call failed with {StatusCode} after {ElapsedMs} ms: {Body}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, Truncate(body, 500));
                throw new AiServiceUnavailableException(
                    $"The AI backend returned {(int)response.StatusCode} ({response.StatusCode}).");
            }

            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                ?? throw new AiServiceUnavailableException("The AI backend returned an empty response.");
            root = document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as a cancellation, so distinguish it
            // from a genuine caller-initiated abort.
            logger.LogError("Ollama chat call timed out after {ElapsedMs} ms (limit {TimeoutSeconds}s).",
                stopwatch.ElapsedMilliseconds, options.Value.RequestTimeout.TotalSeconds);
            throw new AiServiceUnavailableException(
                $"The AI backend did not respond within {options.Value.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Unable to reach the Ollama backend at {BaseUrl} after {ElapsedMs} ms.",
                options.Value.BaseUrl, stopwatch.ElapsedMilliseconds);
            throw new AiServiceUnavailableException("The AI backend is unreachable.", ex);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Ollama returned a malformed chat payload after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            throw new AiServiceUnavailableException("The AI backend returned a malformed response.", ex);
        }

        var result = ParseChatResponse(root);
        stopwatch.Stop();

        // One log line per model round trip. The orchestrator only records total turn time,
        // which hides how much of a slow turn was the model versus tool execution.
        LogLatency(
            "chat",
            options.Value.EffectiveChatModel,
            stopwatch.ElapsedMilliseconds,
            result.PromptTokens,
            result.CompletionTokens,
            result.ToolCalls.Count);

        return result;
    }

    private JsonObject BuildChatRequest(IReadOnlyList<AiCompletionMessage> messages, IReadOnlyList<AiToolDefinition> tools)
    {
        var request = new JsonObject
        {
            ["model"] = options.Value.EffectiveChatModel,
            ["stream"] = false
        };

        var messageArray = new JsonArray();
        foreach (var message in messages)
        {
            var node = new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content ?? string.Empty
            };
            // Ollama echoes the tool name back so the model can correlate observations.
            if (!string.IsNullOrWhiteSpace(message.ToolName))
                node["name"] = message.ToolName;
            messageArray.Add(node);
        }
        request["messages"] = messageArray;

        if (tools.Count > 0)
        {
            var toolArray = new JsonArray();
            foreach (var tool in tools)
            {
                // The schema is authored as raw JSON, so it must be parsed rather than
                // assigned as a string or the model receives an escaped blob.
                JsonNode? parameters;
                try
                {
                    parameters = JsonNode.Parse(tool.ParametersJsonSchema);
                }
                catch (JsonException ex)
                {
                    throw new AiAgentException($"Tool '{tool.Name}' declares an invalid JSON Schema.", ex);
                }

                toolArray.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = parameters ?? new JsonObject { ["type"] = "object" }
                    }
                });
            }
            request["tools"] = toolArray;
        }

        return request;
    }

    private static AiCompletionResult ParseChatResponse(JsonElement root)
    {
        var content = string.Empty;
        var toolCalls = new List<AiToolInvocation>();

        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                content = contentElement.GetString() ?? string.Empty;

            if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                        continue;
                    if (!function.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
                        continue;

                    var name = nameElement.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    toolCalls.Add(new AiToolInvocation(name, ReadArguments(function)));
                }
            }
        }

        return new AiCompletionResult(
            content,
            toolCalls,
            ReadInt(root, "prompt_eval_count"),
            ReadInt(root, "eval_count"));
    }

    /// <summary>
    /// Normalizes the arguments payload. Ollama emits a JSON object, while some
    /// OpenAI-compatible shims emit a JSON-encoded string; both are accepted.
    /// </summary>
    private static string ReadArguments(JsonElement function)
    {
        if (!function.TryGetProperty("arguments", out var arguments))
            return "{}";

        return arguments.ValueKind switch
        {
            JsonValueKind.Object => arguments.GetRawText(),
            JsonValueKind.String => NormalizeArgumentString(arguments.GetString()),
            JsonValueKind.Null or JsonValueKind.Undefined => "{}",
            _ => "{}"
        };
    }

    private static string NormalizeArgumentString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "{}";
        try
        {
            using var parsed = JsonDocument.Parse(raw);
            return parsed.RootElement.ValueKind == JsonValueKind.Object ? parsed.RootElement.GetRawText() : "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }

    private static int? ReadInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value)
            ? value
            : null;

    // ---------------------------------------------------------------------
    // IAiEmbeddingClient
    // ---------------------------------------------------------------------

    /// <summary>
    /// Calls POST /api/embeddings and returns the raw vector. Callers persist it via
    /// <see cref="EmbeddingSerializer"/>.
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input must not be empty.", nameof(text));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/embeddings",
                new { model = options.Value.EmbeddingModel, prompt = text },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Ollama embedding call failed with {StatusCode} after {ElapsedMs} ms: {Body}",
                    (int)response.StatusCode, stopwatch.ElapsedMilliseconds, Truncate(body, 500));
                throw new AiServiceUnavailableException(
                    $"The embedding backend returned {(int)response.StatusCode} ({response.StatusCode}).");
            }

            var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(ResponseSerializerOptions, cancellationToken);
            if (payload?.Embedding is not { Length: > 0 })
                throw new AiServiceUnavailableException("The embedding backend returned an empty vector.");

            stopwatch.Stop();
            // The retrieval embedding is on the critical path of every chat turn, so it is
            // timed separately from the chat completion it precedes.
            LogLatency("embeddings", options.Value.EmbeddingModel, stopwatch.ElapsedMilliseconds);

            return payload.Embedding;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Ollama embedding call timed out after {ElapsedMs} ms (limit {TimeoutSeconds}s).",
                stopwatch.ElapsedMilliseconds, options.Value.RequestTimeout.TotalSeconds);
            throw new AiServiceUnavailableException(
                $"The embedding backend did not respond within {options.Value.RequestTimeout.TotalSeconds:0} seconds.");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Unable to reach the Ollama embedding endpoint at {BaseUrl} after {ElapsedMs} ms.",
                options.Value.BaseUrl, stopwatch.ElapsedMilliseconds);
            throw new AiServiceUnavailableException("The embedding backend is unreachable.", ex);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Ollama returned a malformed embedding payload after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
            throw new AiServiceUnavailableException("The embedding backend returned a malformed response.", ex);
        }
    }

    /// <summary>
    /// Emits one structured timing record per backend round trip, escalating to Warning past
    /// <see cref="OllamaOptions.SlowRequestLogThreshold"/> so slow AI calls are visible at the
    /// default log level.
    /// </summary>
    private void LogLatency(
        string operation,
        string model,
        long elapsedMs,
        int? promptTokens = null,
        int? completionTokens = null,
        int toolCalls = 0)
    {
        var level = elapsedMs >= options.Value.SlowRequestLogThreshold ? LogLevel.Warning : LogLevel.Information;
        logger.Log(
            level,
            "Ollama {Operation} finished in {ElapsedMs} ms (model {Model}, promptTokens {PromptTokens}, completionTokens {CompletionTokens}, toolCalls {ToolCalls}).",
            operation, elapsedMs, model, promptTokens, completionTokens, toolCalls);
    }

    private sealed record EmbeddingResponse([property: JsonPropertyName("embedding")] float[]? Embedding);

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}
