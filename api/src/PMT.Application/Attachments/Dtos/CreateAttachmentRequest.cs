namespace PMT.Application.Attachments.Dtos;
public sealed record CreateAttachmentRequest(string EntityType, long EntityId, string FileName, string StoredFileName, string ContentType, long FileSize, string StoragePath);
