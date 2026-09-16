namespace PMT.Application.AiAgent.Dtos;

/// <summary>
/// A destructive action the agent wants to take, held back until the user approves it.
/// </summary>
/// <param name="ToolName">The tool that will run on approval, for example <c>delete_story</c>.</param>
/// <param name="ArgumentsJson">The exact arguments that will be replayed. Nothing is re-derived on approval.</param>
/// <param name="Summary">
/// A human-readable description of the change, for example
/// <c>Delete story #1289 "Login with Google"</c>. This is what the confirmation dialog shows.
/// </param>
public sealed record AgentPendingActionDto(string ToolName, string? ArgumentsJson, string? Summary);

/// <summary>
/// Inbound payload for POST /api/v1/ai/agent/confirm: the user's answer to a pending
/// destructive action.
/// </summary>
/// <param name="Token">The one-time token returned with the pending actions.</param>
/// <param name="Action">Either <c>approve</c> or <c>cancel</c>.</param>
public sealed record AgentConfirmRequestDto(string Token, string Action)
{
    /// <summary>Value of <see cref="Action"/> that executes the pending actions.</summary>
    public const string Approve = "approve";

    /// <summary>Value of <see cref="Action"/> that discards the pending actions.</summary>
    public const string Cancel = "cancel";
}
