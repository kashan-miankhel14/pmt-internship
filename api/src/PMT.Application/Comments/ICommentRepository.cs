using PMT.Domain.Entities;

namespace PMT.Application.Comments;

public interface ICommentRepository
{
    Task<IReadOnlyCollection<Comment>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default);
    Task<Comment?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Comment entity, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Comment entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? user, CancellationToken cancellationToken = default);
}
