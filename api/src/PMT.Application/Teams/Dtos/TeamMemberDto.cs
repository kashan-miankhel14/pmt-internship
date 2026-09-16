namespace PMT.Application.Teams.Dtos;

public sealed class TeamMemberDto
{
    public long TeamId { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string TeamRole { get; set; } = string.Empty;
    public DateTime JoinedAtUtc { get; set; }
}