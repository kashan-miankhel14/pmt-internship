using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PMT.Application.AiAgent;
using PMT.Application.AiAgent.Models;

namespace PMT.Infrastructure.Ai;

/// <summary>Point-in-time view of the reindex worker.</summary>
/// <param name="IsRunning">True while a rebuild is in flight.</param>
/// <param name="StartedAt">When the current or most recent run began.</param>
/// <param name="CompletedAt">When the most recent run finished.</param>
/// <param name="LastResult">Counters from the most recent successful run.</param>
/// <param name="LastError">Failure message from the most recent run, if it faulted.</param>
public sealed record AiReindexStatus(
    bool IsRunning,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    ReindexResult? LastResult,
    string? LastError);

/// <summary>Outcome of an awaited reindex request.</summary>
/// <param name="Started">False when the single-flight gate rejected the request.</param>
/// <param name="Result">Counters, when the run completed.</param>
/// <param name="Error">Failure or cancellation message, when it did not.</param>
/// <remarks>
/// Faults are reported here rather than thrown: callers of
/// <see cref="AiReindexRunner.RunReindexAsync"/> are background loops that must not die because
/// one rebuild failed, and the runner has already logged and recorded the failure by the time
/// this is returned.
/// </remarks>
public sealed record AiReindexRun(bool Started, ReindexResult? Result, string? Error)
{
    /// <summary>A request that never ran because another rebuild held the gate.</summary>
    public static readonly AiReindexRun Rejected = new(false, null, null);
}

/// <summary>
/// Runs full reindexes off the request thread.
/// </summary>
/// <remarks>
/// A rebuild embeds every indexable entity and can easily outlive an HTTP request, so the
/// admin endpoint starts it here and returns immediately. Single-flight: a second trigger
/// while a run is active is rejected rather than queued, so an impatient admin cannot
/// stack duplicate rebuilds against the embedding backend. The same gate covers the scheduled
/// rebuilds started by <see cref="AiAutoReindexService"/>, which awaits its run through
/// <see cref="RunReindexAsync"/> instead of detaching it.
/// </remarks>
public sealed class AiReindexRunner(IServiceScopeFactory scopeFactory, ILogger<AiReindexRunner> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateLock = new();

    private bool _isRunning;
    private DateTime? _startedAt;
    private DateTime? _completedAt;
    private ReindexResult? _lastResult;
    private string? _lastError;

    public AiReindexStatus GetStatus()
    {
        lock (_stateLock)
            return new AiReindexStatus(_isRunning, _startedAt, _completedAt, _lastResult, _lastError);
    }

    /// <summary>
    /// Starts a rebuild if none is active. Returns false when one is already running.
    /// </summary>
    public bool TryStart(bool force)
    {
        if (!TryBeginRun()) return false;

        // Deliberately detached: the caller gets 202 and polls for status. The request's
        // CancellationToken is not used, since aborting the HTTP call must not abort the run.
        _ = Task.Run(() => RunAsync(force, CancellationToken.None));
        return true;
    }

    /// <summary>
    /// Runs a rebuild and awaits it, subject to the same single-flight gate as
    /// <see cref="TryStart"/>. Returns <see cref="AiReindexRun.Rejected"/> without waiting when
    /// another rebuild — admin-triggered or scheduled — is already in flight.
    /// </summary>
    /// <remarks>
    /// For callers that own the run's lifetime, such as the hosted scheduler: passing the host's
    /// stopping token lets a shutdown abort the rebuild instead of holding it open, which
    /// <see cref="TryStart"/> deliberately cannot do.
    /// </remarks>
    public async Task<AiReindexRun> RunReindexAsync(bool force, CancellationToken cancellationToken = default)
        => TryBeginRun() ? await RunAsync(force, cancellationToken) : AiReindexRun.Rejected;

    /// <summary>Claims the single-flight gate and marks the run as started.</summary>
    private bool TryBeginRun()
    {
        if (!_gate.Wait(0)) return false;

        lock (_stateLock)
        {
            _isRunning = true;
            _startedAt = DateTime.UtcNow;
            _completedAt = null;
            _lastError = null;
        }

        return true;
    }

    private async Task<AiReindexRun> RunAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            // A fresh scope is required: the triggering request's scope is disposed as soon
            // as the response is written.
            await using var scope = scopeFactory.CreateAsyncScope();
            var indexingService = scope.ServiceProvider.GetRequiredService<IAiIndexingService>();
            var result = await indexingService.ReindexAllAsync(force, cancellationToken);

            lock (_stateLock)
            {
                _lastResult = result;
                _lastError = null;
            }

            return new AiReindexRun(true, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not a fault: the index is simply left where the run got to, and the
            // next run picks the remaining sources up because they are still stale.
            const string message = "Reindex was cancelled before it finished.";
            logger.LogInformation("AI reindex run cancelled.");
            lock (_stateLock) _lastError = message;
            return new AiReindexRun(true, null, message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI reindex run failed.");
            lock (_stateLock) _lastError = ex.Message;
            return new AiReindexRun(true, null, ex.Message);
        }
        finally
        {
            lock (_stateLock)
            {
                _isRunning = false;
                _completedAt = DateTime.UtcNow;
            }
            _gate.Release();
        }
    }
}
