using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Comments;
using PMT.Application.Comments.Dtos;
using PMT.Application.Common.Security;

namespace PMT.Api.Controllers.V1;

[Authorize(Policy = PermissionRequirement.CommentsManage)]
[ApiController, Route("api/v1/comments"), EnableRateLimiting("api")]
public sealed class CommentsController(CommentService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<CommentDto>> Get([FromQuery] string entityType, [FromQuery] long entityId, CancellationToken cancellationToken) => service.GetForEntityAsync(entityType, entityId, cancellationToken);
    [HttpPost] public async Task<IActionResult> Create(CreateCommentRequest request, CancellationToken cancellationToken) => Ok(new { id = await service.CreateAsync(request, cancellationToken) });
    [HttpPut("{id:long}")] public async Task<IActionResult> Update(long id, UpdateCommentRequest request, CancellationToken cancellationToken) { await service.UpdateAsync(request with { Id = id }, cancellationToken); return NoContent(); }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken) { await service.DeleteAsync(id, cancellationToken); return NoContent(); }
}
