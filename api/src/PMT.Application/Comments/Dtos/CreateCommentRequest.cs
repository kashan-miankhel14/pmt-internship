namespace PMT.Application.Comments.Dtos;
public sealed record CreateCommentRequest(string EntityType, long EntityId, string Body);
