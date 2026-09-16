using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Sprints;

public interface ISprintRepository
{
    Task<PagedResult<Sprint>> GetPagedAsync(long projectId, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<Sprint?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Sprint entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Sprint entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flips a PLANNED sprint to ACTIVE. Returns the number of sprints actually
    /// transitioned, which is 0 when the sprint was not PLANNED.
    /// </summary>
    Task<long> StartAsync(long id, long? startedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flips an ACTIVE sprint to COMPLETED. Returns the number of sprints actually
    /// transitioned, which is 0 when the sprint was not ACTIVE.
    /// </summary>
    Task<long> CompleteAsync(long id, long? completedBy, CancellationToken cancellationToken = default);
}
