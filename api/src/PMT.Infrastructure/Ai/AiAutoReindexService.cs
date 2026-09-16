using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PMT.Infrastructure.Ai;

/// <summary>
/// Keeps the retrieval index fresh on a timer, so search does not depend on an administrator
/// remembering to press the reindex button.
/// </summary>
/// <remarks>
/// <para>Interval-based rather than write-triggered on purpose: a chunk is embedded through a
/// network call to the embedding backend, so hanging one off every entity write would put that
/// call on the request path (or need a queue of its own) for data that is only read by search.
/// Batching the work every few minutes keeps writes fast, collapses repeated edits of the same
/// row into one embedding, and gives the backend a predictable load. The cost is staleness
/// bounded by the interval, which retrieval tolerates. <c>IAiIndexingService.UpsertAsync</c>
/// remains available for anything that ever does need immediate indexing.</para>
/// <para>Runs go through <see cref="AiReindexRunner"/> rather than calling the indexing service
/// directly, so a scheduled run and an admin-triggered one share a single-flight gate: whichever
/// starts first wins, and the other is skipped rather than queued. The admin endpoint keeps
/// answering 409 while a scheduled run is in flight.</para>
/// </remarks>
public sealed class AiAutoReindexService(
    AiReindexRunner runner,
    IOptions<AiAutoReindexOptions> options,
    ILogger<AiAutoReindexService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadOptions();

        if (!settings.IsEnabled)
        {
            // Registered unconditionally so enabling it is configuration-only; returning here is
            // what makes it free when it is off.
            logger.LogInformation(
                "AI auto reindex is disabled. Set '{Section}:IsEnabled' to true to schedule it.",
                AiAutoReindexOptions.SectionName);
            return;
        }

        logger.LogInformation(
            "AI auto reindex enabled: {Mode} rebuild every {Interval}, first run one interval after startup.",
            settings.Incremental ? "incremental" : "full",
            settings.Interval);

        // PeriodicTimer waits a full interval before the first tick, which is what we want: the
        // host is still warming up (connections, caches, the embedding client) at startup.
        using var timer = new PeriodicTimer(settings.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(settings.Force, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        catch (Exception ex)
        {
            // Nothing above is expected to throw, but an escaping exception would take the host
            // down with the default BackgroundServiceExceptionBehavior. A stale index is not
            // worth that, so the loop ends quietly instead.
            logger.LogWarning(ex, "AI auto reindex loop stopped unexpectedly; the index will no longer refresh itself.");
        }
    }

    private async Task RunOnceAsync(bool force, CancellationToken stoppingToken)
    {
        try
        {
            var run = await runner.RunReindexAsync(force, stoppingToken);

            if (!run.Started)
            {
                // The admin endpoint (or a previous tick that is still going) holds the gate.
                // Skipping is correct: the work would be the same work, and the next tick is
                // only an interval away.
                logger.LogInformation("AI auto reindex skipped: a reindex is already running.");
                return;
            }

            if (run.Result is { } result)
            {
                logger.LogInformation(
                    "AI auto reindex completed: {Indexed} indexed, {Skipped} skipped, {Failed} failed in {Elapsed}.",
                    result.Indexed, result.Skipped, result.Failed, result.Duration);
                return;
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                // The runner has already logged the exception; this is the one-line operator
                // view. Warning, not Error: the next tick retries, and a backend that is down
                // for a few minutes should not read as a failure of the API.
                logger.LogWarning("AI auto reindex did not complete: {Error}", run.Error);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI auto reindex tick failed; the next tick will retry.");
        }
    }

    /// <summary>Reads the section, falling back to the compiled defaults (disabled) if it cannot be bound.</summary>
    /// <remarks>
    /// Binding throws when a value has the wrong shape (for example <c>"IntervalMinutes": "15m"</c>).
    /// This runs during host start, where an unhandled exception stops the application, so a typo
    /// in an optional background job must not be able to prevent the API from starting.
    /// </remarks>
    private AiAutoReindexOptions ReadOptions()
    {
        try
        {
            return options.Value;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The '{Section}' configuration section could not be bound; auto reindex stays disabled.",
                AiAutoReindexOptions.SectionName);

            return new AiAutoReindexOptions();
        }
    }
}
