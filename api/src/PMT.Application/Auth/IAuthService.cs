using PMT.Application.Auth.Dtos;
using PMT.Domain.Common;

namespace PMT.Application.Auth;

public interface IAuthService
{
    Task<Result<LoginResult>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResult>> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task RevokeAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResult>> RefreshWithReuseDetectionAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default);
    Task RevokeAllAsync(long userId, string? ipAddress, CancellationToken cancellationToken = default);
}
