namespace PMT.Application.AiAgent.Models;

/// <summary>A message in the prompt sent to the LLM (not necessarily persisted).</summary>
/// <param name="Role">One of user, assistant, system or tool.</param>
/// <param name="Content">Message body.</param>
/// <param name="ToolName">Tool that produced the content, when <paramref name="Role"/> is "tool".</param>
public sealed record AiCompletionMessage(string Role, string Content, string? ToolName = null)
{
    public static AiCompletionMessage System(string content) => new("system", content);
    public static AiCompletionMessage User(string content) => new("user", content);
    public static AiCompletionMessage Assistant(string content) => new("assistant", content);
    public static AiCompletionMessage Tool(string toolName, string content) => new("tool", content, toolName);
}

/// <summary>A function the model is allowed to call.</summary>
/// <param name="Name">Snake-case tool name exposed to the model.</param>
/// <param name="Description">What the tool does and when to use it.</param>
/// <param name="ParametersJsonSchema">JSON Schema object describing the arguments.</param>
public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>A tool call requested by the model.</summary>
/// <param name="Name">Requested tool name.</param>
/// <param name="ArgumentsJson">Arguments as a JSON object string.</param>
public sealed record AiToolInvocation(string Name, string ArgumentsJson);

/// <summary>One completion returned by the LLM.</summary>
/// <param name="Content">Assistant text; empty when the model only requested tools.</param>
/// <param name="ToolCalls">Tools the model wants executed before it can answer.</param>
/// <param name="PromptTokens">Prompt token count when reported by the backend.</param>
/// <param name="CompletionTokens">Completion token count when reported by the backend.</param>
public sealed record AiCompletionResult(
    string Content,
    IReadOnlyList<AiToolInvocation> ToolCalls,
    int? PromptTokens = null,
    int? CompletionTokens = null)
{
    public bool HasToolCalls => ToolCalls.Count > 0;
    public int TotalTokens => (PromptTokens ?? 0) + (CompletionTokens ?? 0);
}
