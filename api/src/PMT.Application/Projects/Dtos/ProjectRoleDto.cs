namespace PMT.Application.Projects.Dtos;

public sealed class ProjectRoleDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
}