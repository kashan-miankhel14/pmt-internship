using FluentValidation;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Realtime;
using PMT.Application.Notifications.Dtos;
using PMT.Domain.Entities;
using PMT.Domain.Exceptions;

namespace PMT.Application.Notifications;

public sealed class NotificationService(
    INotificationRepository repository,
    IValidator<CreateNotificationRequest> validator,
    ICurrentUserService currentUser,
    IRealtimeNotifier notifier)
{
    public async Task<IReadOnlyCollection<NotificationDto>> GetMineAsync(bool unreadOnly, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        return (await repository.GetForUserAsync(userId, unreadOnly, cancellationToken)).Select(Map).ToArray();
    }

    /// <summary>
    /// Stores a notification and pushes it to its recipient.
    /// </summary>
    /// <remarks>
    /// The request reaches this method straight from the controller's model binder, so every
    /// string on it is only non-null by convention: a body that omits "title" binds it as null
    /// and a bare <c>.Trim()</c> would throw a NullReferenceException as a 500. The required
    /// fields are rejected by <see cref="Validators.CreateNotificationValidator"/> first, and the
    /// null-coalescing here is the second line of defence for any caller that bypasses it.
    /// </remarks>
    public async Task<long> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            throw new PMT.Domain.Exceptions.ValidationException(validation.Errors.Select(x => x.ErrorMessage));

        var entity = new Notification
        {
            UserId = request.UserId,
            // Notification.Type is an alias over EventType, the persisted column, so it is set once.
            EventType = request.Type?.Trim() ?? string.Empty,
            Title = request.Title?.Trim() ?? string.Empty,
            Message = request.Message?.Trim() ?? string.Empty,
            Link = request.Link?.Trim(),
            InsertedBy = currentUser.UserId
        };
        var id = await repository.CreateAsync(entity, cancellationToken);

        // The pushed payload is shaped exactly like the one GetMineAsync returns, so a client can
        // prepend it to its cached list without the row changing appearance after the next refetch.
        // InsertDate is stamped by the procedure; UtcNow is the closest value available here.
        await notifier.NotifyUserAsync(entity.UserId, RealtimeEvents.Notification,
            new NotificationDto(id, entity.UserId, entity.Type, Headline(entity.Title), entity.Message, entity.Link, false, DateTime.UtcNow),
            cancellationToken);
        return id;
    }

    public async Task MarkReadAsync(long id, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        if (!await repository.MarkReadAsync(id, userId, cancellationToken))
            throw new NotFoundException(nameof(Notification), id);
    }

    private static NotificationDto Map(Notification x)
        => new(x.Id, x.UserId, x.Type, Headline(x.Title), x.Message, x.Link, x.IsRead, x.InsertDate);

    /// <summary>
    /// Reports a missing title as null rather than as an empty string. Notification.Title is
    /// nullable in the database and rows written before it was part of the insert have none, so the
    /// client can fall back to the type instead of rendering a blank headline.
    /// </summary>
    private static string? Headline(string? title) => string.IsNullOrWhiteSpace(title) ? null : title;
}
