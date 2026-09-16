using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;

namespace PMT.Infrastructure.BackgroundJobs;

public sealed class JobQueueWorker(IServiceScopeFactory scopeFactory, ILogger<JobQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IJobQueueRepository>();
                var job = await repository.ClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                try
                {
                    await ProcessAsync(scope.ServiceProvider, job.JobType, job.PayloadJson, stoppingToken);
                    await repository.CompleteAsync(job.Id, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background job {JobId} ({JobType}) failed", job.Id, job.JobType);
                    var delayMinutes = Math.Min(60, (int)Math.Pow(2, Math.Max(1, job.Attempts)));
                    await repository.FailAsync(job.Id, ex.Message, DateTime.UtcNow.AddMinutes(delayMinutes), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job queue polling failed");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private static async Task ProcessAsync(IServiceProvider services, string jobType, string payloadJson, CancellationToken cancellationToken)
    {
        switch (jobType.Trim().ToLowerInvariant())
        {
            case "email":
                var email = JsonSerializer.Deserialize<EmailJobPayload>(payloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Invalid email job payload.");
                await services.GetRequiredService<IEmailService>().SendAsync(email.To, email.Subject, email.HtmlBody, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Unsupported background job type '{jobType}'.");
        }
    }

    private sealed record EmailJobPayload(string To, string Subject, string HtmlBody);
}
