using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PMT.Application.Common.Interfaces;

namespace PMT.Infrastructure.Persistence;

/// <summary>
/// Creates pooled SQL Server connections from a connection string that is resolved and
/// pool-tuned once, at construction.
/// </summary>
/// <remarks>
/// Every repository method opens its own connection, so this type is on the hot path of every
/// request. Resolving the connection string here instead of per call removes a configuration
/// provider walk from each of them, and pre-tuning the pool keeps a warm set of connections so
/// a burst of requests does not pay for a TCP/TLS/auth handshake per connection.
/// </remarks>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        var configured = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        _connectionString = ApplyPoolDefaults(configured, configuration);
    }

    public DbConnection CreateConnection() => new SqlConnection(_connectionString);

    /// <summary>
    /// Fills in pool settings that the supplied connection string does not already specify.
    /// </summary>
    /// <remarks>
    /// An explicitly configured value always wins, so a tuned production connection string is
    /// never overridden. <c>ShouldSerialize</c> is used rather than <c>ContainsKey</c> because
    /// <see cref="SqlConnectionStringBuilder"/> reports every known keyword as "contained"
    /// whether or not it was actually supplied.
    /// </remarks>
    private static string ApplyPoolDefaults(string connectionString, IConfiguration configuration)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);

        if (!builder.ShouldSerialize("Pooling"))
            builder.Pooling = true;

        // A warm floor of connections: the first requests after an idle period reuse an open
        // connection instead of handshaking. Costs a handful of idle sessions on the server.
        if (!builder.ShouldSerialize("Min Pool Size"))
            builder.MinPoolSize = configuration.GetValue("Database:MinPoolSize", 5);

        // Headroom above the ADO.NET default of 100. Exhausting the pool surfaces as a
        // connect timeout under load, which is far more expensive than the extra sessions.
        if (!builder.ShouldSerialize("Max Pool Size"))
            builder.MaxPoolSize = configuration.GetValue("Database:MaxPoolSize", 200);

        if (!builder.ShouldSerialize("Connect Timeout"))
            builder.ConnectTimeout = configuration.GetValue("Database:ConnectTimeoutSeconds", 15);

        // Makes PMT sessions identifiable in sys.dm_exec_sessions when diagnosing blocking.
        if (!builder.ShouldSerialize("Application Name"))
            builder.ApplicationName = "PMT.Api";

        return builder.ConnectionString;
    }
}
