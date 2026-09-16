using System.Text.Json;
using PMT.Application.Common.Security;
using PMT.Application.Notifications;

namespace PMT.Application.AiAgent.Tools;

/// <summary>Reads the current user's own notifications.</summary>
/// <remarks>
/// <para>Mirrors <c>GET api/v1/notifications/mine?unreadOnly</c>. The recipient is always
/// <see cref="AgentToolContext.UserId"/> and is never an argument: accepting one would turn a
/// notifications.manage claim into a way to read other people's inbox.</para>
/// <para><b>Titles are optional.</b> The write path persists Title and Link, but the columns are
/// nullable and rows written before they were part of the insert carry neither. The values are
/// returned when present and the payload says so when they are not, rather than letting the model
/// report an empty headline as if it were the notification's real title.</para>
/// <para>SP_NOTIFICATION's FETCH branch returns TOP(200) rows newest first and takes no paging
/// arguments, so page/pageSize slice that pool here and <c>incomplete</c> is reported when the pool
/// is full and older notifications may exist beyond it.</para>
/// </remarks>
public sealed class GetMyNotificationsTool(INotificationRepository repository) : IAgentTool
{
    /// <summary>Rows SP_NOTIFICATION's FETCH branch returns at most.</summary>
    private const int FetchCeiling = 200;

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    public string Name => "get_my_notifications";

    public string Description =>
        "List the current user's own notifications, newest first, with the type, message, link and "
        + "whether each one has been read. Set unreadOnly to true for just the unread ones, and use "
        + "page and pageSize to walk a long list. Older notifications may have no stored title, so "
        + "fall back to the message text.";

    public string ParametersJsonSchema => $$"""
        {
          "type": "object",
          "properties": {
            "unreadOnly": { "type": "boolean", "description": "True to return only unread notifications. Defaults to false." },
            "page": { "type": "integer", "description": "1-based page number. Defaults to 1." },
            "pageSize": { "type": "integer", "description": "Notifications per page (1-{{MaxPageSize}}). Defaults to {{DefaultPageSize}}." }
          },
          "required": []
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
            return AgentToolResult.Fail("No authenticated user, so there are no notifications to read.");

        if (!TryGetBoolean(arguments, "unreadOnly", out var unreadOnly))
            return AgentToolResult.Fail("'unreadOnly' must be true or false.");

        var page = ToolArguments.GetInt(arguments, "page", 1, 1, 1000);
        var pageSize = ToolArguments.GetInt(arguments, "pageSize", DefaultPageSize, 1, MaxPageSize);

        var pool = (await repository.GetForUserAsync(context.UserId, unreadOnly, cancellationToken))
            .Where(x => !x.IsDeleted)
            // The procedure already returns newest first; the ordering is re-applied so paging
            // stays stable regardless of what the procedure does later.
            .OrderByDescending(x => x.InsertDate)
            .ThenByDescending(x => x.Id)
            .ToArray();

        var results = pool
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                id = x.Id,
                type = x.Type,
                title = string.IsNullOrWhiteSpace(x.Title) ? null : x.Title,
                message = x.Message,
                link = x.Link,
                isRead = x.IsRead,
                receivedAt = x.InsertDate,
                readAt = x.ReadDate
            })
            .ToArray();

        return AgentToolResult.Ok(new
        {
            count = results.Length,
            page,
            pageSize,
            totalCount = pool.Length,
            unreadOnly,
            unreadCount = pool.Count(x => !x.IsRead),
            // The procedure caps its result set, so a full pool means older rows were not read.
            incomplete = pool.Length >= FetchCeiling,
            userId = context.UserId,
            titleNote = results.Any(x => x.title is null)
                ? "Some notifications have no stored title; use their message text."
                : null,
            notifications = results
        });
    }

    /// <summary>
    /// Reads an optional boolean the way the model is likely to write it: a real JSON boolean, the
    /// strings "true"/"false", or 1/0. Anything else is a genuine mistake and is reported.
    /// </summary>
    private static bool TryGetBoolean(JsonElement arguments, string name, out bool value)
    {
        value = false;

        var raw = ToolArguments.GetString(arguments, name);
        if (raw is null) return !ToolArguments.Has(arguments, name);

        switch (raw.ToLowerInvariant())
        {
            case "true" or "yes" or "1":
                value = true;
                return true;

            case "false" or "no" or "0":
                value = false;
                return true;

            default:
                return false;
        }
    }
}
