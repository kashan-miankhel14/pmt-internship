using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.Users;

public interface IUserRepository
{
    Task<PagedResult<User>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(User entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(User entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Role>> GetAvailableRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Role>> GetUserRolesAsync(long userId, CancellationToken cancellationToken = default);
    Task<bool> SetUserRolesAsync(long userId, IReadOnlyCollection<long> roleIds, long? updatedBy, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordAsync(long id, string passwordHash, long? updatedBy, CancellationToken cancellationToken = default);
}
