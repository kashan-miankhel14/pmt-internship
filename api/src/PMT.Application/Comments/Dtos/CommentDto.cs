namespace PMT.Application.Comments.Dtos;
public sealed record CommentDto(long Id, string EntityType, long EntityId, long UserId, string Body, DateTime InsertDate);
