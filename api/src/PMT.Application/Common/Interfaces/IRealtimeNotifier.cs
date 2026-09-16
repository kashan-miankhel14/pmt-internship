namespace PMT.Application.Common.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(long userId, string eventName, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <paramref name="eventName"/> to every connection in <paramref name="groupName"/>.
    /// </summary>
    /// <remarks>
    /// Group membership is self-service (clients join through the hub), so it is a delivery channel
    /// and not an authorization decision: only payloads that every authenticated user may see belong
    /// here. See <c>PMT.Application.Common.Realtime.RealtimeGroups</c> for the naming.
    /// </remarks>
    Task NotifyGroupAsync(string groupName, string eventName, object payload, CancellationToken cancellationToken = default);
}
