namespace PMT.Domain.Exceptions;

/// <summary>
/// Base exception for failures inside the AI agent pipeline. Mapped to HTTP 500.
/// </summary>
public class AiAgentException : Exception
{
    public AiAgentException(string message) : base(message) { }
    public AiAgentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Raised when the configured LLM backend is unreachable, times out, is misconfigured, or
/// returns an unusable payload. Mapped to HTTP 503 so clients can offer a retry.
/// </summary>
/// <remarks>
/// Configuration problems belong here too, not in the base <see cref="AiAgentException"/>: a
/// missing API key or an unusable model id is an "the assistant cannot answer right now"
/// condition with an actionable message, and reporting it as a 500 hides both facts.
/// </remarks>
public sealed class AiServiceUnavailableException : AiAgentException
{
    public AiServiceUnavailableException(string message) : base(message) { }
    public AiServiceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
