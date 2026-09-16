using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Dtos;
using PMT.Application.Common.Models;
using PMT.Infrastructure.Ai;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Conversational AI agent endpoints.
/// </summary>
/// <remarks>
/// Authorization model: any authenticated user may chat, and the agent then acts strictly
/// within that user's own permission set (enforced per tool call by the orchestrator).
/// There is no dedicated "ai.view" permission because the 17 canonical permission keys are
/// seeded in SQL; adding an 18th would require a migration before any role could hold it.
/// </remarks>
[Authorize]
[ApiController, Route("api/v1/ai/agent"), EnableRateLimiting("ai-agent")]
[Produces("application/json")]
public sealed class AiAgentController(
    IAiAgentOrchestrator orchestrator,
    IAiChatService chatService,
    AiReindexRunner reindexRunner) : ControllerBase
{
    /// <summary>Sends a message to the agent and returns its reply.</summary>
    /// <response code="200">The agent produced a reply.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="404">The referenced session does not exist or is not yours.</response>
    /// <response code="503">The AI backend is unavailable.</response>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatResponseDto>> Chat([FromBody] ChatRequestDto request, CancellationToken cancellationToken)
        => Ok(await orchestrator.RunAsync(request, cancellationToken));

    /// <summary>Approves or cancels a destructive action the agent asked about.</summary>
    /// <remarks>
    /// A chat turn that would delete something stops short and returns
    /// <c>requiresConfirmation</c> with the pending actions and a one-time token. Post that token
    /// back here with <c>approve</c> to carry the deletions out, or <c>cancel</c> to drop them.
    /// The token is valid for ten minutes, works once, and only for the user it was issued to.
    /// </remarks>
    /// <response code="200">The pending actions were carried out or cancelled.</response>
    /// <response code="400">The token is unknown, expired, already used, or the action is not approve/cancel.</response>
    /// <response code="404">The referenced session no longer exists or is not yours.</response>
    /// <response code="503">The AI backend is unavailable.</response>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(ChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ChatResponseDto>> Confirm([FromBody] AgentConfirmRequestDto request, CancellationToken cancellationToken)
        => Ok(await orchestrator.ConfirmAsync(request, cancellationToken));

    /// <summary>Lists the current user's conversations, most recently active first.</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(PagedResult<ChatSessionDto>), StatusCodes.Status200OK)]
    public Task<PagedResult<ChatSessionDto>> GetSessions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => chatService.GetSessionsAsync(page, pageSize, cancellationToken);

    /// <summary>Returns the full transcript of one of the current user's conversations.</summary>
    /// <response code="404">The session does not exist or belongs to another user.</response>
    [HttpGet("sessions/{id:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyCollection<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyCollection<ChatMessageDto>>> GetMessages(Guid id, CancellationToken cancellationToken)
        => Ok(await chatService.GetMessagesAsync(id, cancellationToken));

    /// <summary>Creates an empty conversation.</summary>
    [HttpPost("sessions")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var id = await chatService.CreateSessionAsync(request?.Title, request?.ProjectId, cancellationToken);
        return CreatedAtAction(nameof(GetMessages), new { id }, new { id });
    }

    /// <summary>Soft-deletes one of the current user's conversations.</summary>
    /// <response code="404">The session does not exist or belongs to another user.</response>
    [HttpDelete("sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSession(Guid id, CancellationToken cancellationToken)
    {
        await chatService.DeleteSessionAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Triggers a full rebuild of the retrieval index.</summary>
    /// <remarks>
    /// Returns 202 immediately: a rebuild embeds every indexable entity and would exceed the
    /// request timeout. Poll the status endpoint for progress. Concurrent triggers are
    /// rejected with 409 rather than queued.
    /// </remarks>
    /// <response code="202">The rebuild has started.</response>
    /// <response code="409">A rebuild is already running.</response>
    [Authorize(Roles = AdminRole)]
    [HttpPost("admin/reindex")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Reindex([FromQuery] bool force = false)
    {
        if (!reindexRunner.TryStart(force))
            return Conflict(new { title = "A reindex is already in progress.", status = StatusCodes.Status409Conflict });

        return Accepted(reindexRunner.GetStatus());
    }

    /// <summary>Reports the state of the most recent or in-flight reindex.</summary>
    [Authorize(Roles = AdminRole)]
    [HttpGet("admin/reindex/status")]
    [ProducesResponseType(typeof(AiReindexStatus), StatusCodes.Status200OK)]
    public ActionResult<AiReindexStatus> ReindexStatus() => Ok(reindexRunner.GetStatus());

    /// <summary>Role seeded by migration 0009 with the full permission set.</summary>
    private const string AdminRole = "Administrator";

    public sealed record CreateSessionRequest(string? Title, long? ProjectId);
}
