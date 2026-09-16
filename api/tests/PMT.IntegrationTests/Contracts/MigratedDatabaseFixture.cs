using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace PMT.IntegrationTests.Contracts;

/// <summary>
/// Creates a throw-away LocalDB database and runs the canonical deployment chain
/// (0009 → 0010 → 0011) against it, exactly the way a fresh environment is
/// provisioned. Every batch is executed, so a migration that is not internally
/// consistent (a constraint that references a column that does not exist yet, a
/// CREATE PROCEDURE that is not first in its batch, …) fails the fixture and
/// therefore every contract test in the class.
///
/// One database per test class: xUnit creates a single fixture instance for the
/// class and shares it across its tests, which keeps the migration cost to one
/// run per class while still isolating classes from one another.
/// </summary>
public sealed class MigratedDatabaseFixture : IDisposable
{
    private const string Server = @"(localdb)\MSSQLLocalDB";
    private const string BaseConnectionString = $"Server={Server};Integrated Security=true;TrustServerCertificate=true;";

    /// <summary>The migration files that make up the canonical fresh-database path, in run order.</summary>
    public static readonly string[] MigrationChain =
    [
        "0009_DeployCompleteSchema.sql",
        "0010_CriticalFixes.sql",
        "0011_RestoredBackupCompatibility.sql"
    ];

    private readonly string _databaseName;
    private bool _disposed;

    public MigratedDatabaseFixture()
    {
        _databaseName = $"PMT_ContractTest_{Guid.NewGuid():N}";
        ConnectionString = $"Server={Server};Database={_databaseName};Integrated Security=true;TrustServerCertificate=true;";

        CreateDatabase();
        try
        {
            RunMigrationChain();
        }
        catch
        {
            DropDatabase();
            throw;
        }
    }

    public string ConnectionString { get; }

    public SqlConnection OpenConnection()
    {
        var connection = new SqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static string MigrationsDirectory => Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..", "..", "..", "..", "..", "database", "migrations"));

    private void CreateDatabase()
    {
        using var connection = new SqlConnection(BaseConnectionString);
        connection.Open();
        using var command = new SqlCommand($"CREATE DATABASE [{_databaseName}]", connection);
        command.ExecuteNonQuery();
    }

    private void RunMigrationChain()
    {
        foreach (var file in MigrationChain)
            RunSqlFile(Path.Combine(MigrationsDirectory, file));
    }

    private void RunSqlFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Migration file not found: {path}");

        var sql = File.ReadAllText(path);
        // Split on GO batch separators (sqlcmd convention: line containing only GO)
        var batches = Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline)
            .Select(b => b.Trim())
            .Where(b => !string.IsNullOrEmpty(b))
            .ToArray();

        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        for (var i = 0; i < batches.Length; i++)
        {
            try
            {
                using var command = new SqlCommand(batches[i], connection);
                command.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} failed in batch {i + 1}/{batches.Length}: {ex.Message}", ex);
            }
        }
    }

    private void DropDatabase()
    {
        try
        {
            using var connection = new SqlConnection(BaseConnectionString);
            connection.Open();
            using var command = new SqlCommand(
                $"IF DB_ID('{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END",
                connection);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Swallow cleanup errors — the CI environment may not have LocalDB.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        SqlConnection.ClearAllPools();
        DropDatabase();
        _disposed = true;
    }
}
