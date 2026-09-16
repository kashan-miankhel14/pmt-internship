namespace PMT.Application.Common.Realtime;

/// <summary>
/// Names of the events pushed over the <c>/hubs/notifications</c> SignalR hub. They are part of
/// the client contract, so they live here rather than as literals at the call sites.
/// </summary>
public static class RealtimeEvents
{
    /// <summary>
    /// One in-app notification addressed to a single user. Payload is a
    /// <c>PMT.Application.Notifications.Dtos.NotificationDto</c>.
    /// </summary>
    public const string Notification = "notification";

    /// <summary>
    /// A project-scoped hint that an entity was written. Payload is an
    /// <see cref="EntityChangedEvent"/>; it carries no entity data, only enough for a client to
    /// decide whether to refetch.
    /// </summary>
    public const string EntityChanged = "entityChanged";
}

/// <summary>
/// Naming of the SignalR groups the server broadcasts to.
/// </summary>
/// <remarks>
/// <para>
/// A group is a change-notification channel, not an authorization boundary: any authenticated
/// connection can join any project group, so nothing sent to one may be sensitive. Everything
/// published to a project group is therefore limited to identifiers the client must re-read
/// through the ordinary, permission-checked REST endpoints before it can display anything.
/// </para>
/// <para>
/// Keys are folded to upper case so a join issued with the key as it appears in a URL matches
/// the group the server publishes to, whatever case the caller used.
/// </para>
/// </remarks>
public static class RealtimeGroups
{
    /// <summary>Prefix of every project change-feed group.</summary>
    public const string ProjectPrefix = "project:";

    /// <summary>Group carrying the change feed of the project addressed by <paramref name="projectKey"/>.</summary>
    public static string Project(string projectKey)
        => ProjectPrefix + projectKey.Trim().ToUpperInvariant();
}
