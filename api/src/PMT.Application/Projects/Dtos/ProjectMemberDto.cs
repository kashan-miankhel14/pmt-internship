namespace PMT.Application.Projects.Dtos;

public sealed class ProjectMemberDto
{
    public long ProjectId { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public long ProjectRoleId { get; set; }
    public string ProjectRoleName { get; set; } = string.Empty;
    public long AddedBy { get; set; }
    public DateTime InsertDate { get; set; }
}