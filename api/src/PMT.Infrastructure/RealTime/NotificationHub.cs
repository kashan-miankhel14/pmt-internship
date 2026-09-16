using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PMT.Application.Common.Realtime;
namespace PMT.Infrastructure.RealTime;

/// <summary>
/// Hub behind <c>/hubs/notifications</c>. It carries two things: the per-user
/// <see cref="RealtimeEvents.Notification"/> push, which needs no client call because SignalR
/// addresses it by user id, and the per-project <see cref="RealtimeEvents.EntityChanged"/> feed,
/// which a client opts into with <see cref="JoinProject"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Project groups are a change-notification channel, not an access grant.</b> Membership is
/// self-service: any authenticated connection may join any project key, and nothing here checks
/// project membership. That is deliberate and is safe only because the payloads sent to a project
/// group contain no sensitive data — an entity type, an id, the project key and a verb. A client
/// still has to fetch the entity through the REST API, where its permissions are evaluated, before
/// it can show anything. Never publish entity content to a project group.
/// </para>
/// <para>
/// Joins are not persisted across reconnects: SignalR drops group membership with the connection,
/// so clients re-join on every <c>onreconnected</c>.
/// </para>
/// </remarks>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>
    /// Longest project key accepted by a join. Keys are validated at 15 characters on the write
    /// side; the same ceiling here stops a client from filling the group registry with junk names.
    /// </summary>
    private const int MaxProjectKeyLength = 15;

    /// <summary>Subscribes this connection to the change feed of <paramref name="projectKey"/>.</summary>
    public Task JoinProject(string projectKey)
        => Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Project(Validate(projectKey)), Context.ConnectionAborted);

    /// <summary>Unsubscribes this connection from the change feed of <paramref name="projectKey"/>.</summary>
    public Task LeaveProject(string projectKey)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Project(Validate(projectKey)), Context.ConnectionAborted);

    /// <summary>
    /// Rejects a key that could never name a real project. A <see cref="HubException"/> is the one
    /// error type whose message SignalR relays to the caller, so the client sees why the join failed.
    /// </summary>
    private static string Validate(string projectKey)
    {
        var trimmed = projectKey?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxProjectKeyLength)
            throw new HubException($"A project key of 1 to {MaxProjectKeyLength} characters is required.");

        return trimmed;
    }
}
