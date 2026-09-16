using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Tasks;

public interface ITaskRepository
{
    Task<PagedResult<TaskItem>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<TaskItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(TaskItem entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(TaskItem entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);

    /// <summary>True when the story has no open child tasks (every task is Done or Cancelled).</summary>
    Task<bool> AreChildTasksResolvedAsync(long storyId, CancellationToken cancellationToken = default);
}
