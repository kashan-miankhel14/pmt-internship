using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Notifications;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Marks one of the current user's notifications as read.</summary>
/// <remarks>
/// <para>Mirrors <c>POST api/v1/notifications/{id}/read</c>. SP_NOTIFICATION's MARKREAD branch
/// updates <c>WHERE Id=@Id AND UserId=@UserId</c>, so the caller's id is passed from
/// <see cref="AgentToolContext.UserId"/> and a notification belonging to somebody else simply
/// updates nothing. The procedure cannot distinguish "no such notification" from "not yours", which
/// is why the API answers 404 to both; the failure message says so plainly rather than implying the
/// record does not exist.</para>
/// <para>Re-marking an already-read notification succeeds: the row still matches, so the update is
/// idempotent.</para>
/// </remarks>
public sealed class MarkNotificationReadTool(INotificationRepository repository) : IAgentTool
{
    public string Name => "mark_notification_read";

    public string Description =>
        "Mark one of the current user's notifications as read, by its notificationId. Only the "
        + "caller's own notifications can be marked; get the id from get_my_notifications first.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "notificationId": { "type": "integer", "description": "Id of the notification to mark read. It must belong to the current user." }
          },
          "required": ["notificationId"]
        }
        """;

    public string? RequiredPermission => PermissionRequirement.NotificationsManage;

    public bool IsDestructive => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (arguments.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
            return AgentToolResult.Fail("Arguments must be a JSON object.");

        if (context.UserId <= 0)
            return AgentToolResult.Fail("No authenticated user, so there is no notification to mark read.");

        var notificationId = ToolArguments.GetLong(arguments, "notificationId");
        if (notificationId is null or <= 0)
            return AgentToolResult.Fail("'notificationId' is required and must be a positive id.");

        if (!await repository.MarkReadAsync(notificationId.Value, context.UserId, cancellationToken))
            return AgentToolResult.Fail(
                $"Notification {notificationId} was not found for the current user. It either does not "
                + "exist, has been deleted, or belongs to someone else — the API reports all three as a 404.");

        return AgentToolResult.Ok(new
        {
            success = true,
            message = $"Notification #{notificationId} is marked as read.",
            notificationId = notificationId.Value,
            userId = context.UserId,
            isRead = true
        });
    }
}
