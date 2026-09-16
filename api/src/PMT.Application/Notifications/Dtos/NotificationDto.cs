namespace PMT.Application.Notifications.Dtos;

/// <summary>
/// One notification as the bell reads it back. <c>Title</c> and <c>Link</c> are nullable because
/// rows written before those columns were part of the insert carry neither; the client falls back
/// to the type for the headline and simply does not navigate without a link.
/// </summary>
public sealed record NotificationDto(long Id, long UserId, string Type, string? Title, string Message, string? Link, bool IsRead, DateTime InsertDate);
