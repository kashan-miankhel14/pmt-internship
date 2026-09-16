using PMT.Application.AiAgent.Dtos;

namespace PMT.Application.AiAgent;

/// <summary>
/// Runs the agent's reason-and-act loop for a single user turn: retrieve context,
/// call the model, execute any authorized tools, and persist the reply.
/// </summary>
public interface IAiAgentOrchestrator
{
    Task<ChatResponseDto> RunAsync(ChatRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Settles a destructive action the agent parked during <see cref="RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// Approving replays the exact parked tool calls — the deletions plus the non-destructive
    /// remainder of the same batch, which stopped with them — through the normal authorization,
    /// scope and audit path; cancelling discards them all without touching anything. The token is
    /// single-use, bound to the user it was issued to, and short-lived, so a stale or replayed
    /// confirmation deletes nothing, and the conversation's ownership is re-checked before
    /// anything runs.
    /// </remarks>
    Task<ChatResponseDto> ConfirmAsync(AgentConfirmRequestDto request, CancellationToken cancellationToken = default);
}
