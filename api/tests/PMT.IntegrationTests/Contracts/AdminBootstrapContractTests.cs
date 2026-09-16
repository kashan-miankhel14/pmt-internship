using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace PMT.IntegrationTests.Contracts;

/// <summary>
/// SP_ADMIN can only run against a database that holds no users, so it gets its
/// own fixture (and therefore its own freshly migrated database) instead of
/// sharing one with the other contract tests.
/// </summary>
public sealed class AdminBootstrapContractTests(MigratedDatabaseFixture fixture) : IClassFixture<MigratedDatabaseFixture>
{
    [Fact]
    public async Task SP_Admin_Bootstrap_Writes_IsDeleted_And_Required_Defaults()
    {
        await using var conn = await fixture.OpenConnectionAsync();

        // Remove the IsDeleted default so the procedure itself has to supply the value.
        // This is the exact shape of the databases where --bootstrap-admin used to fail
        // with "Cannot insert the value NULL into column 'IsDeleted'".
        await conn.ExecuteAsync(
            @"DECLARE @drop nvarchar(500);
              SELECT @drop = N'ALTER TABLE dbo.[User] DROP CONSTRAINT ' + QUOTENAME(DC.name)
              FROM sys.default_constraints DC
              JOIN sys.columns C ON C.object_id = DC.parent_object_id AND C.column_id = DC.parent_column_id
              WHERE DC.parent_object_id = OBJECT_ID('dbo.[User]') AND C.name = 'IsDeleted';
              IF @drop IS NOT NULL EXEC sp_executesql @drop;");

        var adminId = await conn.ExecuteScalarAsync<long>(
            "dbo.SP_ADMIN",
            new { Action = "BOOTSTRAP", FullName = "Bootstrap Admin", Email = "bootstrap@pmt.local", PasswordHash = "argon2-hash" },
            commandType: CommandType.StoredProcedure);

        Assert.True(adminId > 0);

        var row = await conn.QuerySingleAsync<dynamic>(
            @"SELECT U.Email, U.Active, U.IsDeleted, U.IsLocked, U.FailedLoginAttempts, U.InsertDate, R.Name AS RoleName, U.DepartmentId
              FROM dbo.[User] U JOIN dbo.Role R ON R.Id = U.RoleId WHERE U.Id = @id",
            new { id = adminId });

        Assert.Equal("bootstrap@pmt.local", (string)row.Email);
        Assert.True((bool)row.Active);
        Assert.False((bool)row.IsDeleted);
        Assert.False((bool)row.IsLocked);
        Assert.Equal(0, (int)row.FailedLoginAttempts);
        Assert.Equal("Administrator", (string)row.RoleName);
        Assert.NotNull(row.InsertDate);
        Assert.True((long)row.DepartmentId > 0);
    }

    [Fact]
    public async Task SP_Admin_Rejects_Unknown_Actions()
    {
        await using var conn = await fixture.OpenConnectionAsync();
        var failure = await Assert.ThrowsAsync<SqlException>(() => conn.ExecuteScalarAsync<long>(
            "dbo.SP_ADMIN",
            new { Action = "INSERT", FullName = "Nope", Email = "nope@pmt.local", PasswordHash = "hash" },
            commandType: CommandType.StoredProcedure));

        Assert.Contains("INVALID ACTION", failure.Message);
    }
}
