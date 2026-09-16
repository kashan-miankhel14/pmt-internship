using Microsoft.Extensions.Logging;
using PMT.Application.Common.Interfaces;
using PMT.Application.Projects;

namespace PMT.Application.Common.Realtime;

/// <summary>
/// Publishes <see cref="RealtimeEvents.EntityChanged"/> to a project's change-feed group after a
/// write, so clients viewing that project can refetch instead of polling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Best effort by construction.</b> Every failure — an unresolvable project, a hub that cannot
/// be reached, a cancelled request — is swallowed. The write it announces is already committed, so
/// throwing here would turn a successful operation into an error the caller cannot act on. This is
/// the same trade-off the services make around the workflow engine.
/// </para>
/// <para>
/// The publish is awaited rather than detached. Sending to a hub group is an in-process handoff, and
/// detaching it would let the scoped repository used to resolve the project outlive its request
/// scope.
/// </para>
/// <para>
/// The project is read to obtain its public key: the group name and the payload both use the key
/// rather than the surrogate id, because that is what routes and clients address a project by. When
/// the key cannot be resolved, nothing is published rather than a group name that no client joined.
/// </para>
/// <para>
/// Swallowed does not mean invisible: every failure is logged at warning level so a hub or lookup
/// that has stopped working can be found without the symptom (clients that never refresh) having to
/// be reported first.
/// </para>
/// </remarks>
public sealed class EntityChangeBroadcaster(
    IRealtimeNotifier notifier,
    IProjectRepository projects,
    ILogger<EntityChangeBroadcaster> logger)
{
    /// <summary>
    /// Resolves the project's key and publishes the change. Use this from write paths that hold
    /// only the surrogate project id.
    /// </summary>
    public async Task PublishAsync(long projectId, string entityType, long entityId, string action, CancellationToken cancellationToken = default)
    {
        string? projectKey;

        try
        {
            projectKey = (await projects.GetByIdAsync(projectId, cancellationToken))?.Key;
        }
        catch (Exception ex)
        {
            // The entity is already written; a lookup failure only costs the notification.
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not resolve project {ProjectId} to broadcast the {Action} of {EntityType} {EntityId}.", projectId, action, entityType, entityId);

            return;
        }

        await PublishForKeyAsync(projectKey, entityType, entityId, action, cancellationToken);
    }

    /// <summary>
    /// Publishes the change to an already-known project key. Nothing is sent when the key is
    /// missing, because the group name would not match any join.
    /// </summary>
    public async Task PublishForKeyAsync(string? projectKey, string entityType, long entityId, string action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
            return;

        try
        {
            await notifier.NotifyGroupAsync(
                RealtimeGroups.Project(projectKey),
                RealtimeEvents.EntityChanged,
                new EntityChangedEvent(entityType, entityId, projectKey, action),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // See the remarks: a broadcast never fails the write that produced it.
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Could not broadcast the {Action} of {EntityType} {EntityId} to project {ProjectKey}.", action, entityType, entityId, projectKey);
        }
    }
}
