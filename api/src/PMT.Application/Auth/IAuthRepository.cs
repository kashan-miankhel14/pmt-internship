using PMT.Domain.Entities;

namespace PMT.Application.Auth;

public interface IAuthRepository
{
    Task<User?> FindUserAsync(string userNameOrEmail, CancellationToken cancellationToken = default);
    Task<User?> GetUserAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetRolesAsync(long userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(long userId, CancellationToken cancellationToken = default);
    Task RecordLoginSuccessAsync(long userId, CancellationToken cancellationToken = default);
    Task RecordLoginFailureAsync(long userId, int maxAttempts, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(long id, string? replacedByTokenHash, string? revokedByIp, CancellationToken cancellationToken = default);
    Task RevokeAllUserRefreshTokensAsync(long userId, string? revokedByIp, CancellationToken cancellationToken = default);
    /// <summary>
    /// Atomically revokes the refresh token identified by <paramref name="tokenHash"/> (only if it
    /// has not already been revoked) and inserts <paramref name="newTokenHash"/> as its replacement
    /// in a single transaction. Returns <see cref="TokenRotationStatus.NotFound"/> when the token
    /// does not exist or has expired, and <see cref="TokenRotationStatus.ReuseDetected"/> when the
    /// token was already revoked (possible theft).
    /// </summary>
    Task<RefreshRotationResult> RotateRefreshTokenAsync(
        string tokenHash,
        string newTokenHash,
        string jwtId,
        string? createdByIp,
        string? revokedByIp,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);
    Task CleanupRefreshTokensAsync(CancellationToken cancellationToken = default);
}
