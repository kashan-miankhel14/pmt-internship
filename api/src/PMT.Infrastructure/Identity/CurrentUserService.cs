using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PMT.Application.Common.Interfaces;
using PMT.Application.Common.Security;
namespace PMT.Infrastructure.Identity;
public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    private HttpContext? Context => accessor.HttpContext;
    public long? UserId => long.TryParse(Context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? UserName => Context?.User.Identity?.Name;
    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();
    public bool IsAuthenticated => Context?.User.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Permissions =>
        Context?.User.FindAll(PermissionRequirement.ClaimType).Select(x => x.Value).ToArray()
        ?? Array.Empty<string>();

    public IReadOnlyCollection<string> Roles =>
        Context?.User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray()
        ?? Array.Empty<string>();

    public bool HasPermission(string permission) =>
        !string.IsNullOrWhiteSpace(permission)
        && Context?.User.HasClaim(x =>
            x.Type == PermissionRequirement.ClaimType
            && string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase)) == true;

    public bool IsInRole(string role) =>
        !string.IsNullOrWhiteSpace(role) && Context?.User.IsInRole(role) == true;
}
