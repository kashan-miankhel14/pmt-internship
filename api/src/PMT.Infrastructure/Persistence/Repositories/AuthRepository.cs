using System.Data;
using Dapper;
using PMT.Application.Auth;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Entities;
using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence.Repositories;

public sealed class AuthRepository(IDbConnectionFactory f) : IAuthRepository
{
    private static string U => ProcedureNames.Get(StoredProcedure.User);
    private static string R => ProcedureNames.Get(StoredProcedure.RefreshToken);
    private static string A(ProcedureAction a) => ProcedureNames.Action(a);

    public async Task<User?> FindUserAsync(string value, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<User>(new(
            U,
            new { Action = A(ProcedureAction.Fetch), Value = value },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task<User?> GetUserAsync(long id, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<User>(new(
            U,
            new { Action = A(ProcedureAction.Fetch), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(long id, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return (await c.QueryAsync<string>(new(
            U,
            new { Action = A(ProcedureAction.Roles), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsAsync(long id, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return (await c.QueryAsync<string>(new(
            U,
            new { Action = A(ProcedureAction.Permissions), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct))).AsList();
    }

    public async Task RecordLoginSuccessAsync(long id, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteAsync(new CommandDefinition(
            U,
            new { Action = A(ProcedureAction.LoginSuccess), Id = id },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task RecordLoginFailureAsync(long id, int max, TimeSpan duration, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteAsync(new CommandDefinition(
            U,
            new { Action = A(ProcedureAction.LoginFailure), Id = id, Max = max, DurationSeconds = (int)duration.TotalSeconds },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task<RefreshToken?> FindRefreshTokenAsync(string hash, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        return await c.QuerySingleOrDefaultAsync<RefreshToken>(new CommandDefinition(
            R,
            new { Action = A(ProcedureAction.Fetch), TokenHash = hash },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task AddRefreshTokenAsync(RefreshToken t, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteScalarAsync<long>(new CommandDefinition(
            R,
            new
            {
                Action = A(ProcedureAction.Insert),
                t.UserId,
                t.TokenHash,
                t.ExpiryDate,
                JwtId = t.JwtId,
                CreatedByIp = t.CreatedByIp
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task<RefreshRotationResult> RotateRefreshTokenAsync(
        string tokenHash,
        string newTokenHash,
        string jwtId,
        string? createdByIp,
        string? revokedByIp,
        DateTime expiresAt,
        CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        var row = await c.QuerySingleAsync<(int Status, long UserId)>(new CommandDefinition(
            ProcedureNames.AuthRotateRefreshToken,
            new
            {
                TokenHash = tokenHash,
                NewTokenHash = newTokenHash,
                ExpiresAt = expiresAt,
                JwtId = jwtId,
                CreatedByIp = createdByIp,
                RevokedByIp = revokedByIp
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));

        return new RefreshRotationResult((TokenRotationStatus)row.Status, row.UserId);
    }

    public async Task RevokeRefreshTokenAsync(long id, string? replacement, string? ip, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteScalarAsync<long>(new CommandDefinition(
            R,
            new
            {
                Action = A(ProcedureAction.Update),
                Id = id,
                ReplacedByTokenHash = replacement,
                RevokedByIp = ip
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task RevokeAllUserRefreshTokensAsync(long userId, string? revokedByIp, CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteScalarAsync<long>(new CommandDefinition(
            R,
            new
            {
                Action = A(ProcedureAction.RevokeAll),
                UserId = userId,
                RevokedByIp = revokedByIp
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    public async Task CleanupRefreshTokensAsync(CancellationToken ct = default)
    {
        await using var c = f.CreateConnection();
        await c.ExecuteScalarAsync<long>(new CommandDefinition(
            R,
            new { Action = A(ProcedureAction.Cleanup) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }
}
