using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using PMT.Application.Common.Interfaces;
using PMT.Domain.Enums;

namespace PMT.Infrastructure.Persistence;

public sealed class AdminBootstrapper(IDbConnectionFactory f, IPasswordHasher hasher, IConfiguration config)
{
    public async Task<long> BootstrapAsync(CancellationToken ct = default)
    {
        var email = config["BootstrapAdmin:Email"];
        var name = config["BootstrapAdmin:DisplayName"];
        var password = config["BootstrapAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set BootstrapAdmin Email, DisplayName, and Password before using --bootstrap-admin.");

        if (password.Length < 12)
            throw new InvalidOperationException("The bootstrap administrator password must contain at least 12 characters.");

        await using var c = f.CreateConnection();
        return await c.ExecuteScalarAsync<long>(new(
            ProcedureNames.Get(StoredProcedure.Admin),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.Bootstrap),
                FullName = name.Trim(),
                Email = email.Trim().ToLowerInvariant(),
                PasswordHash = hasher.Hash(password)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }

    /// <summary>Resets the password for the first active Administrator found. Requires BootstrapAdmin settings to be configured.</summary>
    public async Task<int> ResetAdminPasswordAsync(CancellationToken ct = default)
    {
        var password = config["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set BootstrapAdmin:Password before using --reset-admin-password.");

        if (password.Length < 12)
            throw new InvalidOperationException("The administrator password must contain at least 12 characters.");

        await using var c = f.CreateConnection();
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            ProcedureNames.Get(StoredProcedure.Admin),
            new
            {
                Action = ProcedureNames.Action(ProcedureAction.ResetPassword),
                PasswordHash = hasher.Hash(password)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
    }
}