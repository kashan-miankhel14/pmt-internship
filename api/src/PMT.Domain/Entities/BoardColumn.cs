using PMT.Domain.Common;

namespace PMT.Domain.Entities;

/// <summary>
/// One column of a project's board. Ordinal drives left-to-right placement and
/// <see cref="CompleteColumn"/> marks the terminal column that means "done".
/// </summary>
public sealed class BoardColumn : AuditableEntity
{
    public long ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public bool CompleteColumn { get; set; }
    public long? CreatedBy { get; set; }
}
