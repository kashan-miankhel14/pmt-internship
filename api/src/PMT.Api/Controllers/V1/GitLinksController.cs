using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.GitLinks;
using PMT.Application.GitLinks.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.GitManage)]
[ApiController, Route("api/v1/git-links"), EnableRateLimiting("api")]
public sealed class GitLinksController(GitLinkService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<GitLinkDto>> Get([FromQuery]string entityType,[FromQuery]long entityId,CancellationToken cancellationToken) => service.GetForEntityAsync(entityType,entityId,cancellationToken);
    [HttpPost] public async Task<IActionResult> Create(CreateGitLinkRequest request,CancellationToken cancellationToken) => Ok(new { id=await service.CreateAsync(request,cancellationToken) });
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id,CancellationToken cancellationToken) { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
