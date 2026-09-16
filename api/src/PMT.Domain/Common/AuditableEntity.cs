namespace PMT.Domain.Common;

public abstract class AuditableEntity
{
    public long Id { get; set; }
    public bool Active { get; set; } = true;
    public DateTime InsertDate { get; set; } = DateTime.UtcNow;
    public long? InsertedBy { get; set; }
    public DateTime? UpdateDate { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public long? DeletedBy { get; set; }
}
