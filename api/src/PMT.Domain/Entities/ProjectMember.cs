using PMT.Domain.Common;


namespace PMT.Domain.Entities;

/// <summary>
/// A direct grant of a <see cref="ProjectRole"/> to a user on a project.
/// Mirrors core.ProjectMembers, whose natural key is (ProjectId, UserId).
/// </summary>
public sealed class ProjectMember : AuditableEntity
{
    public long ProjectId { get; set; }
    public long UserId { get; set; }
    public long ProjectRoleId { get; set; }

    /// <summary>User who issued the grant.</summary>
    public long AddedBy { get; set; }
}
