using PMT.Domain.Common;


namespace PMT.Domain.Entities;

/// <summary>
/// Membership of a user in a <see cref="Team"/>. Mirrors core.TeamMembers,
/// whose natural key is (TeamId, UserId).
/// </summary>
public sealed class TeamMember : AuditableEntity
{
    public long TeamId { get; set; }
    public long UserId { get; set; }

    /// <summary>Role within the team: 'Lead', 'Member' or 'Guest'.</summary>
    public string TeamRole { get; set; } = "Member";

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}
