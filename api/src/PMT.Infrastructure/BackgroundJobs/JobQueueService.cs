using System.Text.Json;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;

namespace PMT.Infrastructure.BackgroundJobs;

public sealed class JobQueueService(IJobQueueRepository repository, ICurrentUserService currentUser) : IJobQueueService
{
    public Task<long> EnqueueAsync(string jobType, object payload, DateTime? availableAt = null, CancellationToken cancellationToken = default)
        => repository.EnqueueAsync(new JobQueueItem
        {
            JobType = jobType.Trim(),
            PayloadJson = JsonSerializer.Serialize(payload),
            AvailableAt = availableAt ?? DateTime.UtcNow,
            InsertedBy = currentUser.UserId
        }, cancellationToken);
}
