using HealthCheckService.Models;

namespace HealthCheckService.Services;

/// <summary>
/// A single thing to check. Implementations both check the current state AND
/// attempt one self-heal action (recycle app pool, start a stopped service, etc.)
/// before reporting failure back up to the retry loop.
/// </summary>
public interface IHealthChecker
{
    /// <summary>Human readable name used in logs and SMS alerts.</summary>
    string CheckName { get; }

    Task<HealthCheckResult> CheckAndAttemptFixAsync(CancellationToken ct);
}
