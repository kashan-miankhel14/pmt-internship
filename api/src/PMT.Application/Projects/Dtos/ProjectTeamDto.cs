namespace PMT.Application.Projects.Dtos;

public sealed class ProjectTeamDto
{
    public long ProjectId { get; set; }
    public long TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string TeamKey { get; set; } = string.Empty;
    public long ProjectRoleId { get; set; }
    public string ProjectRoleName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public DateTime InsertDate { get; set; }
}