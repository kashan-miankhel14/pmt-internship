using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Models;
using PMT.Domain.Exceptions;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Google Gemini client implementing <see cref="IAiChatCompletionClient"/> against the
/// generateContent endpoint. Supports tool calling; embeddings still go through the local
/// OllamaClient.
/// </summary>
/// <remarks>
/// <para>Gemini is not OpenAI-shaped, so this is a translation layer rather than a rename of the
/// previous client:</para>
/// <list type="bullet">
/// <item>There is no system role. System text is lifted out of the transcript into the top-level
/// <c>systemInstruction</c>.</item>
/// <item>The assistant role is called <c>model</c>, and turns carry a <c>parts</c> array rather
/// than a single string, so consecutive same-role messages are merged into one turn.</item>
/// <item>Tool calls arrive as <c>functionCall</c> parts mixed in with text parts, not as a
/// separate tool_calls array.</item>
/// <item>Tool schemas go in a single <c>functionDeclarations</c> list and are validated against
/// an OpenAPI subset, so they are filtered before they are sent (see <see cref="SanitizeSchema"/>).</item>
/// <item>The API key is not a bearer token. It travels in the <c>x-goog-api-key</c> request
/// header, which the Generative Language API supports alongside the <c>?key=</c> query
/// parameter. The header is used deliberately and the query parameter is not: a URI is the one
/// part of a request that everything downstream copies — proxy and gateway access logs,
/// <see cref="HttpRequestException"/> messages, browser and CDN referrers, crash dumps — so a
/// key in the query string leaks by default. A header value is carried by none of those. The
/// key is never logged and never appears in an exception message either way.</item>
/// </list>
/// </remarks>
public sealed class GeminiChatClient : IAiChatCompletionClient
{
    /// <summary>Requests slower than this are logged as warnings.</summary>
    private const long SlowRequestLogThresholdMs = 10_000;

    /// <summary>Total attempts for a retryable failure (rate limit / overloaded).</summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// Header the Generative Language API reads the key from. Used instead of the <c>?key=</c>
    /// query parameter so the credential never becomes part of a URI.
    /// </summary>
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private readonly HttpClient _client;
    private readonly IOptions<GeminiOptions> _options;
    private readonly ILogger<GeminiChatClient> _logger;

    public GeminiChatClient(HttpClient client, IOptions<GeminiOptions> options, ILogger<GeminiChatClient> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<AiCompletionResult> ChatWithToolsAsync(
        IReadOnlyList<AiCompletionMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(messages));

        // Binding failures (a string where a number belongs, a malformed section) surface here
        // as an InvalidOperationException. Left alone that is a bare 500 with a stack trace the
        // operator cannot act on, so it is reported the same way every other backend problem is:
        // a 503 naming the section to fix.
        GeminiOptions options;
        try
        {
            options = _options.Value;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The Gemini configuration could not be bound.");
            throw new AiServiceUnavailableException(
                "The Gemini configuration is invalid. Check the \"Gemini\" section (ApiKey, ChatModel, MaxSteps, RequestTimeoutSeconds).",
                ex);
        }

        // Fail with something the operator can act on rather than letting Google answer 400.
        if (!options.HasApiKey)
            throw new AiServiceUnavailableException(
                "No Gemini API key is configured. Set Gemini:ApiKey (user secrets or environment) to a key from aistudio.google.com/apikey.");

        var apiKey = options.ApiKey.Trim();

        // A key that is not a legal header value cannot be escaped into one: header values have
        // no encoding, so a stray CR/LF would be a header-injection vector rather than the
        // harmless 400 the old query parameter produced. Reject it as the configuration mistake
        // it is, without ever echoing the value into the message.
        if (!IsValidHeaderValue(apiKey))
            throw new AiServiceUnavailableException(
                "The configured Gemini API key contains characters that cannot be sent in a request header. "
                + "Re-copy the key from aistudio.google.com/apikey into Gemini:ApiKey.");

        var model = options.EffectiveChatModel;

        // A model name carrying a slash, a space or a query character would be pasted straight
        // into the request path and come back as an unexplained 400/404 from Google.
        if (!IsValidModelName(model))
            throw new AiServiceUnavailableException(
                $"The configured Gemini model name '{model}' is not valid. Set Gemini:ChatModel to a model id such as {GeminiOptions.DefaultChatModel}.");

        var payload = BuildChatRequest(messages, tools);

        // Key-free by construction: the credential is attached as a header by CreateRequest, so
        // this URI is safe to log, to appear in an HttpRequestException and to be seen by any
        // proxy on the way out. Every attempt below builds its request through that one helper,
        // so a retry cannot reintroduce the key here.
        var requestUri = $"v1beta/models/{model}:generateContent";

        var stopwatch = Stopwatch.StartNew();
        JsonElement root = default;
        var received = false;

        for (var attempt = 1; attempt <= MaxRetries && !received; attempt++)
        {
            try
            {
                // A sent HttpRequestMessage cannot be sent again, so each attempt gets a fresh
                // one from the same helper rather than a mutated copy of the last.
                using var request = CreateRequest(requestUri, apiKey, payload);
                using var response = await _client.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var status = (int)response.StatusCode;

                    // 429 is the free tier's per-minute quota and 503 is "model overloaded";
                    // both usually clear within seconds, so they are retried with backoff
                    // rather than surfaced as a failed turn.
                    if (IsRetryable(status) && attempt < MaxRetries)
                    {
                        var delay = ResolveRetryDelay(body, attempt);
                        _logger.LogWarning(
                            "Gemini returned {StatusCode} (attempt {Attempt}/{MaxRetries}); retrying in {Seconds}s.",
                            status, attempt, MaxRetries, delay);
                        await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                        continue;
                    }

                    _logger.LogError("Gemini chat call failed with {StatusCode} after {ElapsedMs} ms: {Body}",
                        status, stopwatch.ElapsedMilliseconds, Truncate(body, 500));
                    throw new AiServiceUnavailableException(DescribeError(status, body));
                }

                using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken)
                    ?? throw new AiServiceUnavailableException("The AI service returned an empty response.");
                root = document.RootElement.Clone();
                received = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Gemini chat call timed out after {ElapsedMs} ms (limit {TimeoutSeconds}s).",
                    stopwatch.ElapsedMilliseconds, options.RequestTimeout.TotalSeconds);
                throw new AiServiceUnavailableException(
                    $"The AI service did not respond within {options.RequestTimeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Unable to reach the Gemini API after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
                throw new AiServiceUnavailableException("The AI service is unreachable.", ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Gemini returned a malformed payload after {ElapsedMs} ms.", stopwatch.ElapsedMilliseconds);
                throw new AiServiceUnavailableException("The AI service returned a malformed response.", ex);
            }
        }

        // Defensive: the loop either sets root or throws. Never parse a default JsonElement.
        if (!received)
            throw new AiServiceUnavailableException("The AI service did not return a usable response.");

        var result = ParseChatResponse(root);
        stopwatch.Stop();

        _logger.Log(
            stopwatch.ElapsedMilliseconds >= SlowRequestLogThresholdMs ? LogLevel.Warning : LogLevel.Information,
            "Gemini {Model} finished in {ElapsedMs} ms (promptTokens {PromptTokens}, completionTokens {CompletionTokens}, toolCalls {ToolCalls}).",
            model, stopwatch.ElapsedMilliseconds, result.PromptTokens, result.CompletionTokens, result.ToolCalls.Count);

        return result;
    }

    // ------------------------------------------------------------------
    // Request construction
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds one generateContent request: the key-free URI, the JSON body and the
    /// <c>x-goog-api-key</c> header carrying the credential.
    /// </summary>
    /// <remarks>
    /// This is the single place the key is attached to a request, so the initial call and every
    /// retry are constructed identically and no call site can put it back in the URI by accident.
    /// The header value is never logged: the log statements in this class carry the status code,
    /// the response body and the elapsed time only.
    /// </remarks>
    private static HttpRequestMessage CreateRequest(string requestUri, string apiKey, JsonObject payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload)
        };

        // TryAddWithoutValidation: this is a custom (non-standard) header, and the value has
        // already been checked by IsValidHeaderValue.
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey);

        return request;
    }

    /// <summary>
    /// Whether a configured key can legally be sent as an HTTP header value: printable US-ASCII
    /// only, so no control character, CR or LF can be smuggled into the request. The value
    /// itself is never returned, logged or included in the caller's error message.
    /// </summary>
    private static bool IsValidHeaderValue(string value) =>
        value.Length > 0 && value.All(c => c is >= '\u0020' and <= '\u007e');

    /// <summary>
    /// Translates the backend-neutral transcript into a generateContent request.
    /// </summary>
    private static JsonObject BuildChatRequest(IReadOnlyList<AiCompletionMessage> messages, IReadOnlyList<AiToolDefinition> tools)
    {
        var request = new JsonObject();
        var contents = new JsonArray();
        var systemText = new StringBuilder();

        JsonArray? currentParts = null;
        string? currentRole = null;

        foreach (var message in messages)
        {
            var role = message.Role?.Trim().ToLowerInvariant();
            var text = message.Content ?? string.Empty;

            // System text has no role of its own in Gemini; it is hoisted to systemInstruction.
            if (role == "system")
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (systemText.Length > 0) systemText.AppendLine();
                    systemText.Append(text);
                }
                continue;
            }

            if (role == "tool")
            {
                // Deliberately sent as text on a user turn rather than as a functionResponse part.
                // Gemini requires a functionResponse to sit immediately after the model turn that
                // contains the matching functionCall, and the orchestrator replaces that turn with
                // a plain-text summary before handing the transcript back, so the pairing cannot be
                // reconstructed here. An unpaired functionResponse is rejected with a 400, which
                // would break every tool-using turn; a labelled text observation reads the same to
                // the model and always validates.
                text = FormatToolObservation(message.ToolName, text);
                role = "user";
            }
            else
            {
                role = role is "assistant" or "model" ? "model" : "user";
            }

            // A part with empty text is rejected, so drop the message instead of sending it.
            if (string.IsNullOrWhiteSpace(text)) continue;

            var part = new JsonObject { ["text"] = text };

            // Gemini expects turns to alternate; merge runs of the same role into one turn.
            if (currentParts is not null && currentRole == role)
            {
                currentParts.Add(part);
            }
            else
            {
                currentParts = new JsonArray { part };
                contents.Add(new JsonObject { ["role"] = role, ["parts"] = currentParts });
                currentRole = role;
            }
        }

        if (contents.Count == 0)
            throw new AiAgentException("The prompt contained no content to send to the model.");

        request["contents"] = contents;

        if (systemText.Length > 0)
        {
            request["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemText.ToString() } }
            };
        }

        var declarations = BuildFunctionDeclarations(tools);
        if (declarations.Count > 0)
            request["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = declarations } };

        return request;
    }

    /// <summary>Labels a tool result so the model can tell it apart from the user's own words.</summary>
    private static string FormatToolObservation(string? toolName, string content)
    {
        var name = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName;
        var body = string.IsNullOrWhiteSpace(content) ? "{}" : content;
        return $"[tool_result:{name}] {body}";
    }

    /// <summary>
    /// Builds the single functionDeclarations list Gemini expects from the tool registry.
    /// </summary>
    private static JsonArray BuildFunctionDeclarations(IReadOnlyList<AiToolDefinition> tools)
    {
        var declarations = new JsonArray();
        if (tools is null || tools.Count == 0) return declarations;

        foreach (var tool in tools)
        {
            JsonNode? schema;
            try
            {
                schema = JsonNode.Parse(tool.ParametersJsonSchema);
            }
            catch (JsonException ex)
            {
                throw new AiAgentException($"Tool '{tool.Name}' declares an invalid JSON Schema.", ex);
            }

            var declaration = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description
            };

            // An OBJECT schema with no properties is rejected ("should be non-empty for OBJECT
            // type"), so a tool with no arguments is declared without a parameters block at all.
            if (SanitizeSchema(schema) is JsonObject parameters
                && parameters["properties"] is JsonObject properties
                && properties.Count > 0)
            {
                declaration["parameters"] = parameters;
            }

            declarations.Add(declaration);
        }

        return declarations;
    }

    /// <summary>
    /// Keywords Gemini's schema subset understands. Anything else (additionalProperties, $schema,
    /// oneOf, default, ...) is dropped.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemaKeys = new(StringComparer.Ordinal)
    {
        "type", "format", "description", "nullable", "enum", "items", "properties", "required", "minItems", "maxItems"
    };

    /// <summary>
    /// Rewrites a JSON Schema into the OpenAPI subset Gemini accepts.
    /// </summary>
    /// <remarks>
    /// Unlike the OpenAI-compatible endpoint, Gemini rejects the whole request when a declaration
    /// carries a keyword it does not know, so one careless tool schema would take the entire agent
    /// down rather than just its own tool. Filtering to a known-good allowlist keeps a new tool's
    /// mistake local to that tool.
    /// </remarks>
    private static JsonNode? SanitizeSchema(JsonNode? node)
    {
        if (node is not JsonObject source) return null;

        var result = new JsonObject();

        foreach (var (key, value) in source)
        {
            if (value is null || !AllowedSchemaKeys.Contains(key)) continue;

            switch (key)
            {
                case "properties" when value is JsonObject properties:
                {
                    var sanitized = new JsonObject();
                    foreach (var (name, property) in properties)
                    {
                        if (SanitizeSchema(property) is { } child)
                            sanitized[name] = child;
                    }
                    if (sanitized.Count > 0) result["properties"] = sanitized;
                    break;
                }

                case "items":
                {
                    if (SanitizeSchema(value) is { } items) result["items"] = items;
                    break;
                }

                // ["string", "null"] is valid JSON Schema but not valid here: it becomes the
                // first concrete type plus nullable.
                case "type" when value is JsonArray types:
                {
                    foreach (var entry in types)
                    {
                        if (entry is not JsonValue candidate
                            || !candidate.TryGetValue<string>(out var name)
                            || string.IsNullOrWhiteSpace(name))
                            continue;

                        if (string.Equals(name, "null", StringComparison.OrdinalIgnoreCase))
                            result["nullable"] = true;
                        else
                            result["type"] ??= name;
                    }
                    break;
                }

                default:
                    result[key] = value.DeepClone();
                    break;
            }
        }

        // A required entry naming a property that was filtered out is itself rejected.
        if (result["required"] is JsonArray required)
        {
            var properties = result["properties"] as JsonObject;
            var kept = new JsonArray();
            foreach (var entry in required)
            {
                if (entry is JsonValue candidate
                    && candidate.TryGetValue<string>(out var name)
                    && !string.IsNullOrWhiteSpace(name)
                    && properties?[name] is not null)
                {
                    kept.Add(name);
                }
            }

            if (kept.Count > 0) result["required"] = kept;
            else result.Remove("required");
        }

        return result;
    }

    /// <summary>
    /// Whether a configured model id is safe to interpolate into the request path. Gemini model
    /// ids are letters, digits, dots, dashes and underscores (plus the ":latest"-style suffix
    /// some aliases carry); anything else is a configuration mistake rather than a real model.
    /// </summary>
    private static bool IsValidModelName(string model) =>
        !string.IsNullOrWhiteSpace(model)
        && model.Length <= 128
        && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');

    // ------------------------------------------------------------------
    // Response parsing
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads candidates[0].content.parts, which interleaves text and functionCall entries, plus
    /// the usageMetadata token counts.
    /// </summary>
    private static AiCompletionResult ParseChatResponse(JsonElement root)
    {
        var content = new StringBuilder();
        var toolCalls = new List<AiToolInvocation>();
        string? finishReason = null;

        // TryGetProperty throws on a non-object element, and a malformed body must surface as an
        // empty completion rather than an unhandled exception.
        if (root.ValueKind != JsonValueKind.Object)
            return new AiCompletionResult(string.Empty, toolCalls);

        if (root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];

            if (candidate.TryGetProperty("finishReason", out var reason) && reason.ValueKind == JsonValueKind.String)
                finishReason = reason.GetString();

            if (candidate.TryGetProperty("content", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object) continue;

                    // Thinking models emit their scratchpad as text parts flagged "thought".
                    // That is not an answer and must not reach the transcript.
                    if (part.TryGetProperty("thought", out var thought)
                        && thought.ValueKind == JsonValueKind.True)
                        continue;

                    if (part.TryGetProperty("text", out var text)
                        && text.ValueKind == JsonValueKind.String
                        && text.GetString() is { Length: > 0 } value)
                    {
                        if (content.Length > 0) content.Append('\n');
                        content.Append(value);
                    }

                    if (part.TryGetProperty("functionCall", out var call)
                        && call.ValueKind == JsonValueKind.Object
                        && call.TryGetProperty("name", out var name)
                        && name.ValueKind == JsonValueKind.String
                        && name.GetString() is { Length: > 0 } toolName)
                    {
                        toolCalls.Add(new AiToolInvocation(toolName, ReadArguments(call)));
                    }
                }
            }
        }

        // Nothing usable came back: say why, so the turn does not end on a bare
        // "I could not produce a response" when the provider actually refused.
        if (content.Length == 0 && toolCalls.Count == 0)
        {
            var blockReason = ReadBlockReason(root) ?? finishReason;
            var explanation = blockReason?.ToUpperInvariant() switch
            {
                "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "IMAGE_SAFETY" =>
                    "I can't answer that — the response was blocked by the AI provider's safety filters.",
                "MAX_TOKENS" =>
                    "That answer was cut off because it hit the model's output limit. Try asking for something narrower.",
                "RECITATION" =>
                    "I can't answer that — the AI provider blocked the response as recited content.",
                _ => null
            };

            if (explanation is not null) content.Append(explanation);
        }

        int? promptTokens = null;
        int? completionTokens = null;
        if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            promptTokens = ReadInt(usage, "promptTokenCount");
            completionTokens = ReadInt(usage, "candidatesTokenCount");
        }

        return new AiCompletionResult(content.ToString(), toolCalls, promptTokens, completionTokens);
    }

    /// <summary>
    /// Normalizes functionCall.args, which is a JSON object rather than the JSON-encoded string
    /// the OpenAI-compatible endpoint returns.
    /// </summary>
    private static string ReadArguments(JsonElement call)
    {
        if (!call.TryGetProperty("args", out var arguments))
            return "{}";

        return arguments.ValueKind switch
        {
            JsonValueKind.Object => arguments.GetRawText(),
            JsonValueKind.String => NormalizeArgumentString(arguments.GetString()),
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

    /// <summary>Reads promptFeedback.blockReason, set when the prompt itself was refused.</summary>
    private static string? ReadBlockReason(JsonElement root) =>
        root.TryGetProperty("promptFeedback", out var feedback)
        && feedback.ValueKind == JsonValueKind.Object
        && feedback.TryGetProperty("blockReason", out var reason)
        && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;

    private static int? ReadInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value)
            ? value
            : null;

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";

    // ------------------------------------------------------------------
    // Errors and retries
    // ------------------------------------------------------------------

    /// <summary>Rate limited or temporarily overloaded: worth another attempt.</summary>
    private static bool IsRetryable(int status) => status is 429 or 503;

    /// <summary>Turns a failed response into a message that names the actual problem.</summary>
    /// <remarks>
    /// The response body is the only untrusted input here and it is truncated before it is used.
    /// The API key is not available to this method and must never be added to it: these messages
    /// reach the caller as the text of a 503.
    /// </remarks>
    private static string DescribeError(int status, string body)
    {
        var detail = ExtractErrorMessage(body);

        // A bad key is reported as 400 API_KEY_INVALID as often as it is 401/403. Since the key
        // now travels in x-goog-api-key, a missing or malformed header lands here too.
        if (status == 400 && body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase))
            return "Invalid Gemini API key. Create a new key at aistudio.google.com/apikey and set Gemini:ApiKey.";

        return status switch
        {
            400 => $"Gemini rejected the request{Suffix(detail)}",
            401 or 403 => "The Gemini API key was rejected. Check Gemini:ApiKey and that the Generative Language API is enabled for it.",
            404 => $"The configured Gemini model was not found. Check Gemini:ChatModel{Suffix(detail)}",
            429 => "Gemini's rate limit or daily free-tier quota is exhausted. Wait a moment and try again.",
            >= 500 => "Gemini is temporarily unavailable. Please try again in a moment.",
            _ => $"Gemini returned {status}{Suffix(detail)}"
        };

        static string Suffix(string? detail) => string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}";
    }

    /// <summary>Pulls error.message out of the standard Google error envelope.</summary>
    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                    ? Truncate(message.GetString() ?? string.Empty, 300)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// How long to wait before retrying. Google returns a RetryInfo detail carrying a duration
    /// such as "21s" on quota errors; when it is absent the delay backs off exponentially
    /// (2s, 4s, 8s) and is capped so a retry cannot outlive the turn budget.
    /// </summary>
    private static double ResolveRetryDelay(string body, int attempt)
    {
        var suggested = ParseRetryDelay(body);
        var backoff = Math.Pow(2, attempt);
        return Math.Min(Math.Max(suggested ?? backoff, 1), 30);
    }

    private static double? ParseRetryDelay(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details)
                || details.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;
                if (!detail.TryGetProperty("retryDelay", out var retryDelay) || retryDelay.ValueKind != JsonValueKind.String)
                    continue;

                // Protobuf duration: a number of seconds with a trailing "s".
                var value = (retryDelay.GetString() ?? string.Empty).Trim().TrimEnd('s', 'S');
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    return seconds + 1; // small safety margin
            }
        }
        catch (JsonException)
        {
            // Fall through to the exponential backoff.
        }

        return null;
    }
}
