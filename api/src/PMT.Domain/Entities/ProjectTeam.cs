using PMT.Domain.Common;


namespace PMT.Domain.Entities;

/// <summary>
/// A bulk grant of a <see cref="ProjectRole"/> to every member of a <see cref="Team"/>.
/// Mirrors core.ProjectTeams, whose natural key is (ProjectId, TeamId).
/// </summary>
/// <remarks>
/// Removing this grant revokes project access for the team's members unless they
/// also hold a direct <see cref="ProjectMember"/> row.
/// </remarks>
public sealed class ProjectTeam : AuditableEntity
{
    public long ProjectId { get; set; }
    public long TeamId { get; set; }
    public long ProjectRoleId { get; set; }

    /// <summary>User who issued the grant.</summary>
    public long AddedBy { get; set; }
}
