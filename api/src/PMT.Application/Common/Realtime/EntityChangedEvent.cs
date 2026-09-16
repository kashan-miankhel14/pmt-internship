namespace PMT.Application.Common.Realtime;

/// <summary>
/// Payload of the <see cref="RealtimeEvents.EntityChanged"/> event: an entity in a project was
/// written, so any client showing that project should refetch what it needs.
/// </summary>
/// <remarks>
/// Deliberately identifiers only. The event goes to a group whose membership is self-service (see
/// <see cref="RealtimeGroups"/>), so the payload must be readable by anyone who can reach the hub;
/// the title, status and everything else still comes from the permission-checked REST endpoints.
/// </remarks>
/// <param name="EntityType">One of the <see cref="EntityChangeTypes"/> names.</param>
/// <param name="EntityId">Surrogate id of the written row.</param>
/// <param name="ProjectKey">Public key of the owning project, as stored.</param>
/// <param name="Action">One of the <see cref="EntityChangeActions"/> verbs.</param>
public sealed record EntityChangedEvent(string EntityType, long EntityId, string ProjectKey, string Action);

/// <summary>Entity names carried by <see cref="EntityChangedEvent.EntityType"/>.</summary>
public static class EntityChangeTypes
{
    public const string Task = "Task";
    public const string Issue = "Issue";
    public const string UserStory = "UserStory";
    public const string Sprint = "Sprint";
}

/// <summary>
/// Verbs carried by <see cref="EntityChangedEvent.Action"/>. Status changes are reported as
/// <see cref="EntityChangeActions.Updated"/>: a client that cares about the new status has to
/// refetch the entity either way, so a separate verb would only add a case to handle.
/// </summary>
public static class EntityChangeActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}
