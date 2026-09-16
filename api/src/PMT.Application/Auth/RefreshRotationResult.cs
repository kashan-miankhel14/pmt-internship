namespace PMT.Application.Auth;

/// <summary>Describes the outcome of an atomic refresh-token rotation.</summary>
public enum TokenRotationStatus
{
    /// <summary>The presented token was not found (or had already expired).</summary>
    NotFound,

    /// <summary>The presented token had already been revoked. Re-using a revoked token
    /// indicates possible token theft and must trigger full session revocation.</summary>
    ReuseDetected,

    /// <summary>The token was valid and has been revoked, with a replacement inserted.</summary>
    Rotated
}

/// <summary>Result returned by <see cref="IAuthRepository.RotateRefreshTokenAsync"/>.</summary>
/// <param name="Status">The rotation outcome.</param>
/// <param name="UserId">The user id that owns the refresh token.</param>
public sealed record RefreshRotationResult(TokenRotationStatus Status, long UserId);
