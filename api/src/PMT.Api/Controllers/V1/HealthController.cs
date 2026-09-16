using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace PMT.Api.Controllers.V1;
[ApiController, Route("api/v1/health"), AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok(new { status="Healthy", utc=DateTime.UtcNow });
}
