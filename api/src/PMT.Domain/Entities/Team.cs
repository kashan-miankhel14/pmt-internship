using PMT.Domain.Common;


namespace PMT.Domain.Entities;

/// <summary>
/// A reusable group of users that can be granted project access in bulk.
/// Mirrors core.Teams.
/// </summary>
public sealed class Team : AuditableEntity
{
    /// <summary>Short uppercase alphanumeric identifier, unique among non-deleted teams.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Owning user; also seated as a 'Lead' <see cref="TeamMember"/> on creation.</summary>
    public long LeadUserId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>User who created the team.</summary>
    public long CreatedBy { get; set; }
}
