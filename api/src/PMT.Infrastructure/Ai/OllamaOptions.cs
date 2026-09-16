namespace PMT.Infrastructure.Ai;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Legacy single-model setting used by the original IAiAssistant helpers
    /// (story breakdown / thread summary).
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>Model used for tool-calling agent completions.</summary>
    public string ChatModel { get; set; } = "llama3.2";

    /// <summary>Model used to generate retrieval embeddings.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Maximum tool-calling iterations before the agent must answer.</summary>
    public int MaxSteps { get; set; } = 8;

    /// <summary>Per-request HTTP timeout for the Ollama backend.</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// TCP/TLS connect timeout. Deliberately far shorter than
    /// <see cref="RequestTimeoutSeconds"/>: reaching the socket is fast even when generation
    /// is slow, so a long connect means the backend is down and should fail immediately.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Requests slower than this are logged at Warning so AI latency regressions are visible
    /// without turning on Debug logging.
    /// </summary>
    public int SlowRequestLogThresholdMs { get; set; } = 10_000;

    /// <summary>Chat model to use, falling back to <see cref="Model"/> when unset.</summary>
    public string EffectiveChatModel => string.IsNullOrWhiteSpace(ChatModel) ? Model : ChatModel;

    /// <summary>Step budget clamped to a sane range so bad configuration cannot hang a request.</summary>
    public int EffectiveMaxSteps => Math.Clamp(MaxSteps <= 0 ? 8 : MaxSteps, 1, 25);

    public TimeSpan RequestTimeout =>
        TimeSpan.FromSeconds(RequestTimeoutSeconds <= 0 ? 90 : Math.Clamp(RequestTimeoutSeconds, 5, 600));

    /// <summary>
    /// Ceiling on one whole agent turn, however many model calls it takes.
    /// </summary>
    /// <remarks>
    /// <see cref="RequestTimeout"/> bounds a single call to the backend; a turn may make up to
    /// <see cref="EffectiveMaxSteps"/> of them, so without a second budget a slow model could keep
    /// a request open for minutes and the caller would simply wait. Two request timeouts is enough
    /// for a normal tool-using turn (a lookup, then the answer) and short enough that a stuck turn
    /// fails while the user is still watching.
    /// </remarks>
    public TimeSpan TotalTurnTimeout => TimeSpan.FromSeconds(RequestTimeout.TotalSeconds * 2);

    /// <summary>Connect timeout clamped to a sane range.</summary>
    public TimeSpan ConnectTimeout =>
        TimeSpan.FromSeconds(ConnectTimeoutSeconds <= 0 ? 5 : Math.Clamp(ConnectTimeoutSeconds, 1, 60));

    /// <summary>Slow-request log threshold clamped to a sane range.</summary>
    public long SlowRequestLogThreshold =>
        SlowRequestLogThresholdMs <= 0 ? 10_000 : Math.Clamp(SlowRequestLogThresholdMs, 250, 600_000);
}
