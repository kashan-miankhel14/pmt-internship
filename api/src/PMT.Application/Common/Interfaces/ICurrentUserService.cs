namespace PMT.Application.Common.Interfaces;

public interface ICurrentUserService
{
    long? UserId { get; }
    string? UserName { get; }
    string? IpAddress { get; }
    bool IsAuthenticated { get; }

    /// <summary>Permission claims carried by the current access token.</summary>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>Role claims carried by the current access token.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>True when the caller holds the given permission claim.</summary>
    bool HasPermission(string permission);

    /// <summary>True when the caller holds the given role claim.</summary>
    bool IsInRole(string role);
}
