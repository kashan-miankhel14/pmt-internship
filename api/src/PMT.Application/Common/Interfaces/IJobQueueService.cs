namespace PMT.Application.Common.Interfaces;

public interface IJobQueueService
{
    Task<long> EnqueueAsync(string jobType, object payload, DateTime? availableAt = null, CancellationToken cancellationToken = default);
}
