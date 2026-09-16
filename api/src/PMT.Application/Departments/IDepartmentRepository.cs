using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Departments;

public interface IDepartmentRepository
{
    Task<PagedResult<Department>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<Department?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Department entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Department entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
