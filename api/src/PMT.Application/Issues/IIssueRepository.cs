using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Issues;

public interface IIssueRepository
{
    Task<PagedResult<Issue>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<Issue?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Issue entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Issue entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
