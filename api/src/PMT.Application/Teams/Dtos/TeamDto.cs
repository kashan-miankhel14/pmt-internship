namespace PMT.Application.Teams.Dtos;

/// <summary>
/// Dapper-friendly DTO. Dapper needs a parameterless constructor with public setters,
/// so this is a class, not a record.
/// </summary>
public sealed class TeamDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public long LeadUserId { get; set; }
    public string? LeadName { get; set; }
    public int MemberCount { get; set; }
    public DateTime InsertDate { get; set; }
}