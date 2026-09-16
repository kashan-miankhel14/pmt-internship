using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Users;
using PMT.Application.Users.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.UsersView)]
[ApiController, Route("api/v1/users"), EnableRateLimiting("api")]
public sealed class UsersController(UserService service) : ControllerBase
{
    [HttpGet]
    public Task<PMT.Application.Common.Models.PagedResult<UserDto>> Get([FromQuery] int page=1, [FromQuery] int pageSize=25, [FromQuery] string? search=null, CancellationToken cancellationToken=default)
        => service.GetPagedAsync(page,pageSize,search,cancellationToken);
    [HttpGet("{id:long}")]
    public Task<UserDto> GetById(long id, CancellationToken cancellationToken) => service.GetByIdAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpPost]
    public async Task<IActionResult> Create(UpsertUserRequest request, CancellationToken cancellationToken)
    { var id=await service.CreateAsync(request,cancellationToken); return CreatedAtAction(nameof(GetById),new { id },new { id }); }
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpsertUserRequest request, CancellationToken cancellationToken)
    { await service.UpdateAsync(id,request,cancellationToken); return NoContent(); }
    [HttpGet("available-roles")]
    public Task<IReadOnlyCollection<RoleDto>> GetAvailableRoles(CancellationToken cancellationToken)
        => service.GetAvailableRolesAsync(cancellationToken);
    [HttpGet("{id:long}/roles")]
    public Task<IReadOnlyCollection<RoleDto>> GetRoles(long id, CancellationToken cancellationToken)
        => service.GetRolesAsync(id,cancellationToken);
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpPut("{id:long}/roles")]
    public async Task<IActionResult> SetRoles(long id, SetUserRolesRequest request, CancellationToken cancellationToken)
    { await service.SetRolesAsync(id,request,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    { await service.ResetPasswordAsync(id, request, cancellationToken); return NoContent(); }
    [Authorize(Policy=PermissionRequirement.UsersManage)]
    [HttpPost("{id:long}/change-password")]
    public async Task<IActionResult> ChangePassword(long id, [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    { await service.UpdatePasswordAsync(id, request.NewPassword, cancellationToken); return NoContent(); }
}
