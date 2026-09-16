namespace PMT.Application.Attachments.Dtos;
public sealed record AttachmentDto(long Id, string EntityType, long EntityId, string FileName, string ContentType, long FileSize, string StoragePath, DateTime InsertDate);
