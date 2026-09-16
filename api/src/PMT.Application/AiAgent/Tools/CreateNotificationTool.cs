using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Sends an in-app notification to another user.</summary>
/// <remarks>
/// <para>Mirrors <c>POST api/v1/notifications</c> and goes through
/// <see cref="NotificationService.CreateAsync"/> so the recipient also gets the SignalR push the API
/// sends; the service stamps InsertedBy from the caller's identity, so the notification is always
/// attributable to the person Anna is acting for.</para>
/// <para><b>This is the one tool that writes to somebody else's data.</b> Every other write acts on
/// records; this one puts a message in another person's inbox and rings their bell, so the
/// description tells the model to use it only when the user has explicitly asked to notify
/// someone.</para>
/// <para>The title and link are persisted alongside the type and message, so what the recipient
/// sees in the live popup is what they read later in the bell. Older rows, written before the
/// insert carried those columns, still come back with a null title and link.</para>
/// <para>The link is an in-app destination rather than a url, and it is checked here as well as
/// in <c>CreateNotificationValidator</c>: a model that has been talked into sending
/// <c>javascript:...</c> or an off-site address is refused before the notification is written,
/// with a message it can correct itself from.</para>
/// </remarks>
public sealed class CreateNotificationTool(NotificationService service, AgentToolScope scope) : IAgentTool
{
    /// <summary>varchar(50) on Notification.EventType, which backs Type.</summary>
    private const int MaxTypeLength = 50;

    /// <summary>varchar(250) on Notification.Title.</summary>
    private const int MaxTitleLength = 250;

    /// <summary>varchar(500) on Notification.Message and Notification.Link.</summary>
    private const int MaxMessageLength = 500;

    private const int MaxLinkLength = 500;

    public string Name => "create_notification";

    public string Description =>
        "Send an in-app notification to another person: it lands in that user's inbox and pops up live "
        + "for them. Pass the recipient's userId (resolve it with search_users), a short type such as "
        + "'mention' or 'assignment', a title, the message text, and optionally a link. Use this only "
        + "when the user has explicitly asked to notify or ping someone, and read the recipient and "
        + "wording back to them first.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "userId": { "type": "integer", "description": "Id of the person to notify. This is the recipient, not the sender." },
            "type": { "type": "string", "description": "Short category for the notification, for example 'mention', 'assignment' or 'reminder'. Up to {{MaxTypeLength}} characters." },
            "title": { "type": "string", "description": "Short headline shown in the live popup, up to {{MaxTitleLength}} characters." },
            "message": { "type": "string", "description": "The notification text, up to {{MaxMessageLength}} characters. Put the substance here." },
            "link": { "type": "string", "description": "Optional in-app path the recipient can follow, for example '/projects/12'. Must start with '/' and must not be an external url. Up to {{MaxLinkLength}} characters." }
          },
          "required": ["userId", "type", "title", "message"]
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
            return AgentToolResult.Fail("No authenticated user, so there is no sender to attribute this notification to.");

        var userId = ToolArguments.GetLong(arguments, "userId");
        if (userId is null or <= 0)
            return AgentToolResult.Fail("'userId' is required and must be the positive id of the person to notify.");

        // The recipient is a foreign key the procedure does not check, so a typo would silently
        // create a notification nobody can ever see.
        if (await scope.ValidateUserAsync(userId, "userId", cancellationToken) is { } userError)
            return AgentToolResult.Fail(userError);

        var type = ToolArguments.GetString(arguments, "type");
        if (type is null)
            return AgentToolResult.Fail("'type' is required and cannot be blank, for example 'mention' or 'assignment'.");

        if (type.Length > MaxTypeLength)
            return AgentToolResult.Fail($"'type' must be {MaxTypeLength} characters or fewer.");

        var title = ToolArguments.GetString(arguments, "title");
        if (title is null)
            return AgentToolResult.Fail("'title' is required and cannot be blank.");

        if (title.Length > MaxTitleLength)
            return AgentToolResult.Fail($"'title' must be {MaxTitleLength} characters or fewer.");

        var message = ToolArguments.GetString(arguments, "message");
        if (message is null)
            return AgentToolResult.Fail("'message' is required and cannot be blank.");

        if (message.Length > MaxMessageLength)
            return AgentToolResult.Fail($"'message' must be {MaxMessageLength} characters or fewer.");

        var link = ToolArguments.GetString(arguments, "link");
        if (link is { Length: > MaxLinkLength })
            return AgentToolResult.Fail($"'link' must be {MaxLinkLength} characters or fewer.");

        if (link is not null && !IsSiteRelativePath(link))
            return AgentToolResult.Fail(
                "'link' must be a site-relative path such as '/projects/12': it has to start with '/', cannot start with '//', and cannot contain ':'.");

        var id = await service.CreateAsync(new CreateNotificationRequest(userId.Value, type, title, message, link), cancellationToken);
        if (id <= 0)
            return AgentToolResult.Fail("The notification could not be created.");

        return AgentToolResult.Ok(new
        {
            created = true,
            id,
            message = $"User #{userId.Value} was notified: {message}",
            recipientUserId = userId.Value,
            sentByUserId = context.UserId,
            type,
            title,
            notificationText = message,
            link
        });
    }

    /// <summary>
    /// Mirrors the Link rule in <c>CreateNotificationValidator</c>: the value has to be a path
    /// inside this application. A ':' anywhere rejects <c>javascript:</c>, <c>data:</c> and any
    /// absolute <c>https://host</c>; the "not //" clause rejects the protocol-relative form.
    /// </summary>
    private static bool IsSiteRelativePath(string link) =>
        link.StartsWith('/')
        && !link.StartsWith("//", StringComparison.Ordinal)
        && !link.Contains(':', StringComparison.Ordinal);
}
