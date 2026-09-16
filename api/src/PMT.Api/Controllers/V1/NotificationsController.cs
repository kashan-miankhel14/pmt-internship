using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Notifications;
using PMT.Application.Notifications.Dtos;
using PMT.Application.Common.Security;
namespace PMT.Api.Controllers.V1;
[Authorize(Policy=PermissionRequirement.NotificationsManage)]
[ApiController, Route("api/v1/notifications"), EnableRateLimiting("api")]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet("mine")] public Task<IReadOnlyCollection<NotificationDto>> GetMine([FromQuery]bool unreadOnly=false,CancellationToken cancellationToken=default) => service.GetMineAsync(unreadOnly,cancellationToken);
    [HttpPost] public async Task<IActionResult> Create(CreateNotificationRequest request,CancellationToken cancellationToken) => Ok(new { id=await service.CreateAsync(request,cancellationToken) });
    [HttpPost("{id:long}/read")] public async Task<IActionResult> MarkRead(long id,CancellationToken cancellationToken) { await service.MarkReadAsync(id,cancellationToken); return NoContent(); }
}
