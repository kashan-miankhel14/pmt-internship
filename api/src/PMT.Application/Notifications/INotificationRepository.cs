using PMT.Domain.Entities;
namespace PMT.Application.Notifications;
public interface INotificationRepository
{
    Task<IReadOnlyCollection<Notification>> GetForUserAsync(long userId, bool unreadOnly, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Notification entity, CancellationToken cancellationToken = default);
    Task<bool> MarkReadAsync(long id, long userId, CancellationToken cancellationToken = default);
}
