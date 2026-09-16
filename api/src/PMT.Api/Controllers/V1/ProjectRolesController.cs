using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Common.Security;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;

namespace PMT.Api.Controllers.V1;

/// <summary>
/// Exposes the project role lookup that drives role pickers in the UI. The repository is
/// injected directly because this is a read-only lookup with no behaviour to place in a
/// service; <see cref="IProjectRoleRepository"/> is still an Application-layer abstraction.
/// </summary>
[Authorize(Policy = PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/project-roles"), EnableRateLimiting("api")]
public sealed class ProjectRolesController(IProjectRoleRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectRoleDto>>> ListAsync()
        => Ok(await repository.ListAsync());
}
