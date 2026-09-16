using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Projects;
using PMT.Application.Projects.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.ProjectsView)]
[ApiController, Route("api/v1/projects"), EnableRateLimiting("api")]
public sealed class ProjectsController(ProjectService service) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<ProjectDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<ProjectDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.ProjectsManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertProjectRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    /// <summary>
    /// Canonical creation path for the project wizard. The literal "from-template" segment cannot
    /// collide with GET {id:long} (different verb) nor with the parameterless POST (different template).
    /// </summary>
    [Authorize(Policy=PermissionRequirement.ProjectsManage)]
    [HttpPost("from-template")]
    public async Task<IActionResult> CreateFromTemplate(CreateProjectFromTemplateRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateFromTemplateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.ProjectsManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertProjectRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.ProjectsManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
