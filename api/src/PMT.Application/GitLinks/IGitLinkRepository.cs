using PMT.Domain.Entities;
namespace PMT.Application.GitLinks;
public interface IGitLinkRepository
{
    Task<IReadOnlyCollection<GitLink>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(GitLink entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
