namespace PMT.Infrastructure.Ai;

/// <summary>Chat backends the agent loop knows how to talk to.</summary>
public enum AiChatProvider
{
    /// <summary>Local/self-hosted Ollama runtime. The safe default and the rollback target.</summary>
    Ollama = 0,

    /// <summary>Google Gemini generateContent API.</summary>
    Gemini = 1
}

/// <summary>
/// Cross-provider AI settings bound from the "Ai" section.
/// </summary>
/// <remarks>
/// <para>This section exists so switching Anna's chat backend is a configuration change rather
/// than a code change: <c>Ai:ChatProvider</c> picks the <see cref="AiChatProvider"/> used for
/// chat completions, and everything else about the AI stack (embeddings, the RAG index, the
/// tool registry) is deliberately untouched by it.</para>
/// <para>Embeddings are intentionally *not* selectable here. The vectors stored in
/// AiDocumentChunk belong to whichever embedding model produced them, so moving embeddings to
/// another provider would silently poison retrieval until a full reindex. Chat has no such
/// state, which is why only chat is switchable.</para>
/// </remarks>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Active chat backend: "Ollama" (default) or "Gemini". Anything unrecognised — including
    /// a missing section, a typo or an empty string — resolves to Ollama, so a broken value can
    /// never silently point a deployment at a cloud provider it did not ask for.
    /// </summary>
    public string? ChatProvider { get; set; }

    /// <summary>
    /// Hard ceiling, in seconds, on one whole agent turn regardless of which provider is
    /// active and how generous that provider's own budget is.
    /// </summary>
    /// <remarks>
    /// Each provider derives a turn budget from its per-request timeout (Ollama: two request
    /// timeouts, Gemini: three), which at the shipped defaults lands anywhere between two and
    /// four minutes. The client has a single fixed timeout and cannot know which provider is
    /// active, so this ceiling is what keeps the server's budget inside the browser's: the
    /// turn ends with the orchestrator's own "that took too long" message instead of the
    /// client abandoning a request that is still running.
    /// </remarks>
    public int MaxTurnSeconds { get; set; } = 120;

    /// <summary>
    /// Whether <c>&lt;tool_call&gt;</c> tags found in plain model text are executed.
    /// </summary>
    /// <remarks>
    /// Off by default, and it should stay off for any provider with real function calling.
    /// The tag is just text, so anything that can put text in front of the model — a user
    /// message, an older transcript turn, an indexed description — can forge one, whereas a
    /// native tool call is structured data the provider produces itself. Prompt text is
    /// sanitised in the orchestrator as well, but the gate is the actual control: it is only
    /// worth enabling for a backend that ignores the tools parameter entirely.
    /// </remarks>
    public bool AllowTextToolCalls { get; set; }

    /// <summary>The configured provider, defaulting to <see cref="AiChatProvider.Ollama"/>.</summary>
    public AiChatProvider ResolvedChatProvider => Parse(ChatProvider);

    /// <summary><see cref="MaxTurnSeconds"/> clamped to a sane range.</summary>
    public TimeSpan MaxTurnTimeout =>
        TimeSpan.FromSeconds(MaxTurnSeconds <= 0 ? 120 : Math.Clamp(MaxTurnSeconds, 15, 600));

    /// <summary>
    /// Maps a configured provider name onto the enum. Shared by the DI registration (which
    /// reads the raw configuration string so a malformed sibling value cannot break startup)
    /// and by <see cref="AiTurnPolicy"/>, so both always agree on which backend is active.
    /// </summary>
    public static AiChatProvider Parse(string? value) =>
        string.Equals(value?.Trim(), nameof(AiChatProvider.Gemini), StringComparison.OrdinalIgnoreCase)
            ? AiChatProvider.Gemini
            : AiChatProvider.Ollama;
}
