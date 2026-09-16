namespace PMT.Infrastructure.Persistence.Repositories;

/// <summary>Refresh-token persistence is consolidated in AuthRepository because rotation must be atomic with session creation.</summary>
internal sealed class RefreshTokenRepository { }
