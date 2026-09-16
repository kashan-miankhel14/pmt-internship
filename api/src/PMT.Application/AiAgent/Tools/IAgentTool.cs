using System.Text.Json;
using System.Text.Json.Serialization;

namespace PMT.Application.AiAgent.Tools;

/// <summary>
/// Per-request state handed to every tool. Carries the acting user's session so
/// tools can scope their queries; tools must never widen this scope.
/// </summary>
/// <param name="SessionId">Conversation the tool call belongs to.</param>
/// <param name="UserId">The acting user. Tools run as this user, never as a system principal.</param>
/// <param name="ProjectId">Optional project scope hint from the request.</param>
public sealed record AgentToolContext(Guid SessionId, long UserId, long? ProjectId);

/// <summary>Result envelope returned by a tool.</summary>
public sealed record AgentToolResult
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private AgentToolResult(bool succeeded, string payloadJson, string? error)
    {
        Succeeded = succeeded;
        PayloadJson = payloadJson;
        Error = error;
    }

    public bool Succeeded { get; }

    /// <summary>JSON handed back to the model as the tool observation.</summary>
    public string PayloadJson { get; }

    public string? Error { get; }

    public static AgentToolResult Ok(object payload) =>
        new(true, JsonSerializer.Serialize(payload, SerializerOptions), null);

    public static AgentToolResult Fail(string error) =>
        new(false, JsonSerializer.Serialize(new { error }, SerializerOptions), error);
}

/// <summary>
/// A capability the agent may invoke. Implementations are thin adapters over
/// existing application services and are responsible for their own scope checks.
/// </summary>
public interface IAgentTool
{
    /// <summary>Snake-case name exposed to the model.</summary>
    string Name { get; }

    /// <summary>Description the model uses to decide when to call this tool.</summary>
    string Description { get; }

    /// <summary>JSON Schema object describing the accepted arguments.</summary>
    string ParametersJsonSchema { get; }

    /// <summary>
    /// Permission the caller must hold, or null when the tool is available to any
    /// authenticated user. Checked by the orchestrator before <see cref="ExecuteAsync"/>.
    /// </summary>
    string? RequiredPermission { get; }

    /// <summary>
    /// Destructive tools are gated behind an explicit user confirmation step and are
    /// never auto-executed by the loop.
    /// </summary>
    bool IsDestructive { get; }

    /// <summary>Executes the tool. Arguments are the raw JSON object supplied by the model.</summary>
    Task<AgentToolResult> ExecuteAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default);
}
