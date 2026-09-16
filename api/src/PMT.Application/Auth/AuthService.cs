using System.Security.Cryptography;
using System.Text;
using PMT.Application.Auth.Dtos;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Common;
using PMT.Domain.Entities;

namespace PMT.Application.Auth;

public sealed class AuthService(
    IAuthRepository repository,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IDummyHashProvider dummyHashProvider) : IAuthService
{
    private const int MaxFailedAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// The canonical failure message returned for every login rejection so that callers cannot
    /// distinguish "unknown user", "wrong password" or "locked account" from the response alone.
    /// </summary>
    private const string LoginFailureMessage = "Invalid username/email or password.";

    public async Task<Result<LoginResult>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserNameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
            return Result<LoginResult>.Failure("Username/email and password are required.");

        var user = await repository.FindUserAsync(request.UserNameOrEmail.Trim(), cancellationToken);

        // Always run a password-hash verification. For unknown users we verify against a dummy
        // hash so the not-found path takes roughly as long as the wrong-password path, mitigating
        // timing-based user enumeration.
        var candidateHash = user is not null && !string.IsNullOrEmpty(user.PasswordHash)
            ? user.PasswordHash
            : dummyHashProvider.Hash;
        var passwordValid = passwordHasher.Verify(candidateHash, request.Password);

        var isLocked = user is not null && user.IsLocked && user.LockoutEnd > DateTime.UtcNow;

        if (user is null || user.IsDeleted || !user.Active || isLocked || !passwordValid)
        {
            // Record failed-attempt counters only for real, usable, active accounts whose
            // password did not match. This avoids (a) extending an existing lockout and
            // (b) leaking existence of unknown accounts through audit/counters.
            if (user is not null && user.Active && !user.IsDeleted && !isLocked && !passwordValid)
                await repository.RecordLoginFailureAsync(user.Id, MaxFailedAttempts, LockoutDuration, cancellationToken);

            return Result<LoginResult>.Failure(LoginFailureMessage);
        }

        await repository.RecordLoginSuccessAsync(user.Id, cancellationToken);
        return Result<LoginResult>.Success(await CreateSessionAsync(user, ipAddress, cancellationToken));
    }

    public async Task<Result<LoginResult>> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default)
        => await TryRotateRefreshAsync(request, ipAddress, detectReuse: false, cancellationToken);

    /// <summary>
    /// Refresh with token reuse detection.
    /// If a revoked token is presented, it indicates possible token theft.
    /// All refresh tokens for that user are revoked to contain the breach.
    /// </summary>
    public async Task<Result<LoginResult>> RefreshWithReuseDetectionAsync(RefreshRequest request, string? ipAddress, CancellationToken cancellationToken = default)
        => await TryRotateRefreshAsync(request, ipAddress, detectReuse: true, cancellationToken);

    /// <summary>
    /// Validates the presented refresh token, then performs an atomic rotation (revoke the old
    /// token + insert its replacement in a single transaction) using the access-token's JwtId.
    /// When <paramref name="detectReuse"/> is true, presentation of an already-revoked token
    /// (possible theft) revokes the entire user session.
    /// </summary>
    private async Task<Result<LoginResult>> TryRotateRefreshAsync(RefreshRequest request, string? ipAddress, bool detectReuse, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Result<LoginResult>.Failure("Refresh token is required.");

        var hash = HashToken(request.RefreshToken);

        // Cheap pre-check so the common not-found / expired cases do not pay for the atomic
        // rotation transaction. The rotation itself re-validates revocation under a lock,
        // so the read-then-rotate gap is safe.
        var stored = await repository.FindRefreshTokenAsync(hash, cancellationToken);
        if (stored is null || stored.ExpiresAt <= DateTime.UtcNow)
            return Result<LoginResult>.Failure("Refresh token is invalid or expired.");

        var user = await repository.GetUserAsync(stored.UserId, cancellationToken);
        if (user is null || !user.Active || user.IsDeleted ||
            (user.IsLocked && (!user.LockoutEnd.HasValue || user.LockoutEnd > DateTime.UtcNow)))
            return Result<LoginResult>.Failure("User account is unavailable.");

        var roles = await repository.GetRolesAsync(user.Id, cancellationToken);
        var permissions = await repository.GetPermissionsAsync(user.Id, cancellationToken);
        var access = jwtTokenGenerator.Generate(user.Id, user.UserName, user.Email, roles, permissions);

        var nextRawToken = CreateRefreshToken();
        var nextHash = HashToken(nextRawToken);
        var refreshExpiry = DateTime.UtcNow.Add(RefreshLifetime);

        var rotation = await repository.RotateRefreshTokenAsync(
            tokenHash: hash,
            newTokenHash: nextHash,
            jwtId: access.JwtId,
            createdByIp: ipAddress,
            revokedByIp: ipAddress,
            expiresAt: refreshExpiry,
            cancellationToken);

        switch (rotation.Status)
        {
            case TokenRotationStatus.Rotated:
                break;

            case TokenRotationStatus.ReuseDetected:
                if (detectReuse)
                {
                    // Possible token theft: invalidate every outstanding refresh token for the user.
                    await repository.RevokeAllUserRefreshTokensAsync(user.Id, ipAddress, cancellationToken);
                    return Result<LoginResult>.Failure("Refresh token has been revoked. All sessions have been invalidated for security.");
                }
                return Result<LoginResult>.Failure("Refresh token is invalid or expired.");

            default:
                return Result<LoginResult>.Failure("Refresh token is invalid or expired.");
        }

        return Result<LoginResult>.Success(BuildLoginResult(user, roles, permissions, access, nextRawToken, refreshExpiry));
    }

    public async Task RevokeAsync(string refreshToken, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var stored = await repository.FindRefreshTokenAsync(HashToken(refreshToken), cancellationToken);
        if (stored is not null && stored.RevokedAt is null)
            await repository.RevokeRefreshTokenAsync(stored.Id, null, ipAddress, cancellationToken);
    }

    public async Task RevokeAllAsync(long userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        await repository.RevokeAllUserRefreshTokensAsync(userId, ipAddress, cancellationToken);
    }

    private async Task<LoginResult> CreateSessionAsync(
        User user,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var roles = await repository.GetRolesAsync(user.Id, cancellationToken);
        var permissions = await repository.GetPermissionsAsync(user.Id, cancellationToken);
        var access = jwtTokenGenerator.Generate(user.Id, user.UserName, user.Email, roles, permissions);

        var rawRefresh = CreateRefreshToken();
        var refreshExpiry = DateTime.UtcNow.Add(RefreshLifetime);

        // Insert the new refresh token with the access-token JwtId so rotations can be
        // correlated to the access token they were minted alongside.
        await repository.AddRefreshTokenAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefresh),
            JwtId = access.JwtId,
            ExpiresAt = refreshExpiry,
            CreatedByIp = ipAddress
        }, cancellationToken);

        return BuildLoginResult(user, roles, permissions, access, rawRefresh, refreshExpiry);
    }

    private static LoginResult BuildLoginResult(
        User user,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        GeneratedToken access,
        string rawRefresh,
        DateTime refreshExpiry) =>
        new(
            user.Id, user.UserName, user.DisplayName, user.Email,
            access.AccessToken, access.ExpiresAt, rawRefresh, refreshExpiry,
            roles, permissions);

    private static string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
