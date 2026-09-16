namespace PMT.Application.Notifications.Dtos;
public sealed record CreateNotificationRequest(long UserId, string Type, string Title, string Message, string? Link);
