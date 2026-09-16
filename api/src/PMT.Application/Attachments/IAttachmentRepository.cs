using PMT.Domain.Entities;
namespace PMT.Application.Attachments;
public interface IAttachmentRepository
{
    Task<IReadOnlyCollection<Attachment>> GetForEntityAsync(string entityType, long entityId, CancellationToken cancellationToken = default);
    Task<Attachment?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Attachment entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, long? deletedBy, CancellationToken cancellationToken = default);
}
