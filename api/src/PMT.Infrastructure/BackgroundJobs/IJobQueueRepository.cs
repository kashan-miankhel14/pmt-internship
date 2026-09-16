using PMT.Domain.Entities;
namespace PMT.Infrastructure.BackgroundJobs;
public interface IJobQueueRepository
{
    Task<long> EnqueueAsync(JobQueueItem item, CancellationToken cancellationToken = default);
    Task<JobQueueItem?> ClaimNextAsync(CancellationToken cancellationToken = default);
    Task CompleteAsync(long id, CancellationToken cancellationToken = default);
    Task FailAsync(long id, string error, DateTime retryAt, CancellationToken cancellationToken = default);
}
