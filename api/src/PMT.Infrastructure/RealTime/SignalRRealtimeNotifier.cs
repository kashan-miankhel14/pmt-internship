using Microsoft.AspNetCore.SignalR;
using PMT.Application.Common.Interfaces;
namespace PMT.Infrastructure.RealTime;
public sealed class SignalRRealtimeNotifier(IHubContext<NotificationHub> hub) : IRealtimeNotifier
{
    public Task NotifyUserAsync(long userId, string eventName, object payload, CancellationToken cancellationToken = default)
        => hub.Clients.User(userId.ToString()).SendAsync(eventName, payload, cancellationToken);

    public Task NotifyGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken = default)
        => hub.Clients.Group(groupName).SendAsync(eventName, payload, cancellationToken);
}
