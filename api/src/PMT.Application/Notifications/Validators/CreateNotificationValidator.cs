using FluentValidation;
using PMT.Application.Notifications.Dtos;

namespace PMT.Application.Notifications.Validators;

/// <summary>
/// Validates a notification before it is written and pushed. The request arrives straight from
/// the controller's model binder, so nothing on it is guaranteed to be present: the required
/// strings are checked here rather than left to blow up on <c>.Trim()</c> or to be truncated by
/// the varchar columns behind SP_NOTIFICATION.
/// </summary>
/// <remarks>
/// <para>The maximum lengths mirror dbo.Notification exactly: EventType varchar(50),
/// Title varchar(250), Message varchar(500), Link varchar(500). They are measured against the
/// trimmed value, because that is what <see cref="NotificationService.CreateAsync"/> stores.</para>
/// <para><b>Link is deliberately not a url.</b> It is rendered by the web client as an in-app
/// destination, so anything that could leave the site — or run — is rejected: a value must start
/// with a single '/', and a ':' anywhere in it is refused. That one rule covers
/// <c>javascript:</c>, <c>data:</c> and any absolute <c>https://host</c>, while the "not //"
/// clause covers the protocol-relative <c>//evil.example</c> form that a browser would resolve
/// against the current scheme.</para>
/// </remarks>
public sealed class CreateNotificationValidator : AbstractValidator<CreateNotificationRequest>
{
    /// <summary>varchar(50) on Notification.EventType, which backs Type.</summary>
    private const int MaxTypeLength = 50;

    /// <summary>varchar(250) on Notification.Title.</summary>
    private const int MaxTitleLength = 250;

    /// <summary>varchar(500) on Notification.Message.</summary>
    private const int MaxMessageLength = 500;

    /// <summary>varchar(500) on Notification.Link.</summary>
    private const int MaxLinkLength = 500;

    public CreateNotificationValidator()
    {
        RuleFor(x => x.UserId)
            .GreaterThan(0)
            .WithMessage("UserId must refer to the user the notification is for.");

        RuleFor(x => x.Type)
            .NotEmpty()
            .WithMessage("Type is required.")
            .Must(value => Trimmed(value).Length <= MaxTypeLength)
            .WithMessage($"Type must be {MaxTypeLength} characters or fewer.");

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required.")
            .Must(value => Trimmed(value).Length <= MaxTitleLength)
            .WithMessage($"Title must be {MaxTitleLength} characters or fewer.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .WithMessage("Message is required.")
            .Must(value => Trimmed(value).Length <= MaxMessageLength)
            .WithMessage($"Message must be {MaxMessageLength} characters or fewer.");

        // Optional, but when present it has to be a path this application can navigate to.
        RuleFor(x => x.Link)
            .Must(value => Trimmed(value).Length <= MaxLinkLength)
            .WithMessage($"Link must be {MaxLinkLength} characters or fewer.")
            .Must(IsSiteRelativePath)
            .WithMessage("Link must be a site-relative path such as '/projects/12': it has to start with '/', cannot start with '//', and cannot contain ':'.")
            .When(x => !string.IsNullOrWhiteSpace(x.Link));
    }

    /// <summary>
    /// True when the link is a safe in-app destination. Shared in spirit with
    /// <c>CreateNotificationTool</c>, which applies the same three clauses to the value the
    /// agent supplies.
    /// </summary>
    private static bool IsSiteRelativePath(string? link)
    {
        var value = Trimmed(link);

        return value.StartsWith('/')
            && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.Contains(':', StringComparison.Ordinal);
    }

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;
}
