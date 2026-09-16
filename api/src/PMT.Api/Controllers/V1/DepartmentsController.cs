using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Departments;
using PMT.Application.Departments.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.DepartmentsView)]
[ApiController, Route("api/v1/departments"), EnableRateLimiting("api")]
public sealed class DepartmentsController(DepartmentService service) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<DepartmentDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<DepartmentDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.DepartmentsManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertDepartmentRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.DepartmentsManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertDepartmentRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.DepartmentsManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
