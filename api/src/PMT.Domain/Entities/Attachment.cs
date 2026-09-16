using PMT.Domain.Common;


namespace PMT.Domain.Entities;

public sealed class Attachment : AuditableEntity
{
    public long? ProjectId { get; set; }
    public long? TaskId { get; set; }
    public long? IssueId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? FileSizeKb { get; set; }
    public long UploadedByUserId { get; set; }

    public string EntityType { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get => (FileSizeKb ?? 0) * 1024L; set => FileSizeKb = checked((int)Math.Ceiling(value / 1024d)); }
    public string StoragePath { get => FilePath; set => FilePath = value; }
}
