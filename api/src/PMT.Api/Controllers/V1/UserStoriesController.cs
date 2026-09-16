using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.UserStories;
using PMT.Application.UserStories.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.StoriesView)]
[ApiController, Route("api/v1/user-stories"), EnableRateLimiting("api")]
public sealed class UserStoriesController(UserStoryService service) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<UserStoryDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<UserStoryDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.StoriesManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertUserStoryRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.StoriesManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertUserStoryRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.StoriesManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
