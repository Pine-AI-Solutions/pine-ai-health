using HealthCheckService.Models;
using HealthCheckService.Settings;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace HealthCheckService.Services;

/// <summary>
/// Verifies we can open a connection (and run a trivial query) against SQL Server
/// using the configured connection string.
/// </summary>
public class SqlServerHealthChecker : IHealthChecker
{
    private readonly SqlServerOptions _options;
    private readonly ILogger _logger;

    public string CheckName => $"SQL Server ({_options.Name})";

    public SqlServerHealthChecker(SqlServerOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckAndAttemptFixAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new SqlConnection(_options.ConnectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 10;
            await cmd.ExecuteScalarAsync(ct);

            return HealthCheckResult.Ok(CheckName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQL Server health check failed for {Name}", _options.Name);

            // There's nothing on our side to actively "fix" for a SQL Server instance
            // (we don't own the DB service itself here) - the outer retry loop simply
            // tries to reconnect again after the configured delay.
            return HealthCheckResult.Fail(CheckName, $"Could not connect to SQL Server '{_options.Name}': {ex.Message}");
        }
    }
}
