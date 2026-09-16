using PMT.Domain.Common;
using PMT.Domain.Enums;

namespace PMT.Domain.Entities;

/// <summary>
/// A named workflow. The seeded 'Default Jira Workflow' is the one every project is bound to.
/// </summary>
public sealed class Workflow : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
