using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PMT.Application.Auth;
using PMT.Application.Auth.Dtos;
namespace PMT.Api.Controllers.V1;
[ApiController, Route("api/v1/auth")]
public sealed class AuthController(IAuthService service) : ControllerBase
{
    [AllowAnonymous, EnableRateLimiting("login"), HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result=await service.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { errors=result.Errors });
    }
    [AllowAnonymous, EnableRateLimiting("login"), HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var result=await service.RefreshWithReuseDetectionAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Succeeded ? Ok(result.Value) : Unauthorized(new { errors=result.Errors });
    }
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest request, CancellationToken cancellationToken)
    {
        await service.RevokeAsync(request.RefreshToken, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return NoContent();
    }
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAll(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
            return Unauthorized(new { errors = new[] { "Invalid user identity." } });
        await service.RevokeAllAsync(userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = PMT.Application.Common.Security.PermissionRequirement.UsersManage)]
    [HttpPost("cleanup-tokens")]
    public async Task<IActionResult> CleanupTokens([FromServices] PMT.Application.Auth.IAuthRepository authRepo, CancellationToken cancellationToken)
    {
        await authRepo.CleanupRefreshTokensAsync(cancellationToken);
        return NoContent();
    }
}