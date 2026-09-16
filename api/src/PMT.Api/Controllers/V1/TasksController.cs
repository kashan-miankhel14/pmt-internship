using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Tasks;
using PMT.Application.Tasks.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.TasksView)]
[ApiController, Route("api/v1/tasks"), EnableRateLimiting("api")]
public sealed class TasksController(TaskService service) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<TaskDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<TaskDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.TasksManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertTaskRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.TasksManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertTaskRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.TasksManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
