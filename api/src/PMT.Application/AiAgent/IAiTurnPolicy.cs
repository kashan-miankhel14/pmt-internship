namespace PMT.Application.AiAgent;

/// <summary>
/// The limits one agent turn runs under, resolved from whichever chat backend is active.
/// </summary>
/// <remarks>
/// The orchestrator used to read these straight off the Ollama options, which meant a
/// deployment running a different chat provider still budgeted its turns with Ollama's step
/// count and Ollama's timeouts. Behind this abstraction the loop no longer knows or cares
/// which backend answered — it just asks how many steps it may take and how long it has.
/// </remarks>
public interface IAiTurnPolicy
{
    /// <summary>Active chat provider name, for logs and diagnostics only.</summary>
    string ProviderName { get; }

    /// <summary>Maximum model calls in one turn before the agent must answer.</summary>
    int MaxSteps { get; }

    /// <summary>Wall-clock ceiling for the whole turn, however many model calls it takes.</summary>
    TimeSpan TotalTurnTimeout { get; }

    /// <summary>
    /// Whether <c>&lt;tool_call&gt;</c> tags in plain model text may be executed. False unless a
    /// backend without native function calling is in use; native provider tool calls are
    /// unaffected either way.
    /// </summary>
    bool AllowTextToolCalls { get; }
}
