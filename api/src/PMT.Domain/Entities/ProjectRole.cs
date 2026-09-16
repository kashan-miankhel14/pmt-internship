using PMT.Domain.Common;


namespace PMT.Domain.Entities;

/// <summary>
/// A named, project-scoped privilege level (Project Admin / Member / Viewer).
/// Mirrors core.ProjectRoles.
/// </summary>
/// <remarks>
/// <see cref="SortOrder"/> ranks privilege with 1 = highest. Effective-role
/// resolution picks the lowest SortOrder when a user is granted access through
/// both a direct <see cref="ProjectMember"/> row and a <see cref="ProjectTeam"/> grant.
/// </remarks>
public sealed class ProjectRole : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Privilege rank; 1 = highest privilege.</summary>
    public int SortOrder { get; set; }

    /// <summary>Optional UI colour token used by the web client's role chips.</summary>
    public string? ColorKey { get; set; }

    /// <summary>Seeded roles that must not be renamed or deleted by users.</summary>
    public bool IsSystem { get; set; }
}
