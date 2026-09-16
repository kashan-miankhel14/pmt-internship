using PMT.Application.Projects.Dtos;

namespace PMT.Application.Projects;

public interface IProjectRoleRepository
{
    Task<IReadOnlyList<ProjectRoleDto>> ListAsync();
}
