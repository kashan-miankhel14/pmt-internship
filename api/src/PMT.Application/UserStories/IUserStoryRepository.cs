using PMT.Application.Common.Models;
using PMT.Domain.Entities;

namespace PMT.Application.UserStories;

public interface IUserStoryRepository
{
    Task<PagedResult<UserStory>> GetPagedAsync(int page, int pageSize, string? search, CancellationToken cancellationToken = default);
    Task<UserStory?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(UserStory entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(UserStory entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
