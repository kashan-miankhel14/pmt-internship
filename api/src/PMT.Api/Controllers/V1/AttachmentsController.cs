using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Api.Extensions;
using PMT.Application.Attachments;
using PMT.Application.Attachments.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.AttachmentsManage)]
[ApiController, Route("api/v1/attachments"), EnableRateLimiting("api")]
public sealed class AttachmentsController(AttachmentService service) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyCollection<AttachmentDto>> Get([FromQuery]string entityType,[FromQuery]long entityId,CancellationToken cancellationToken) => service.GetForEntityAsync(entityType,entityId,cancellationToken);
    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken)
    {
        var (attachment, physicalPath) = await service.GetDownloadAsync(id, cancellationToken);
        return PhysicalFile(physicalPath, attachment.ContentType, attachment.FileName, enableRangeProcessing: true);
    }
    [HttpPost]
    [RequestSizeLimit(PmtLimits.MaxUploadSizeBytes)]
    public async Task<IActionResult> Create([FromForm] string entityType, [FromForm] long entityId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty.");
        var request = new CreateAttachmentRequest(entityType, entityId, file.FileName, Guid.NewGuid().ToString("N") + Path.GetExtension(file.FileName), file.ContentType, file.Length, "");
        var id = await service.CreateWithFileAsync(request, file.OpenReadStream(), cancellationToken);
        return Ok(new { id });
    }
    [HttpDelete("{id:long}")] public async Task<IActionResult> Delete(long id,CancellationToken cancellationToken) { await service.DeleteAsync(id,cancellationToken); return NoContent(); }
}
