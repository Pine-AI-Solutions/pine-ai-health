using HealthCheckService.Models;
using HealthCheckService.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCheckService.Services;

/// <summary>
/// Runs a single IHealthChecker with retries: up to MaxRetries attempts, waiting
/// RetryDelayMinutes between each. Each attempt already includes the checker's own
/// self-heal action (recycle pool / start service / reconnect). If every attempt
/// fails, an SMS alert is sent describing the issue.
/// </summary>
public class RetryingHealthCheckRunner
{
    private readonly HealthCheckOptions _options;
    private readonly ISmsNotificationService _smsService;
    private readonly ILogger<RetryingHealthCheckRunner> _logger;

    public RetryingHealthCheckRunner(
        IOptions<HealthCheckOptions> options,
        ISmsNotificationService smsService,
        ILogger<RetryingHealthCheckRunner> logger)
    {
        _options = options.Value;
        _smsService = smsService;
        _logger = logger;
    }

    public async Task RunAsync(IHealthChecker checker, CancellationToken ct)
    {
        var maxRetries = _options.MaxRetries <= 0 ? 5 : _options.MaxRetries;
        var delay = TimeSpan.FromMinutes(_options.RetryDelayMinutes <= 0 ? 1 : _options.RetryDelayMinutes);

        HealthCheckResult? lastResult = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            HealthCheckResult result;
            try
            {
                result = await checker.CheckAndAttemptFixAsync(ct);
            }
            catch (Exception ex)
            {
                result = HealthCheckResult.Fail(checker.CheckName, $"Unhandled exception during check: {ex.Message}");
                _logger.LogError(ex, "Unhandled exception while running check '{Check}'", checker.CheckName);
            }

            lastResult = result;

            if (result.Success)
            {
                if (attempt > 1)
                    _logger.LogInformation("'{Check}' recovered on attempt {Attempt}/{Max}.", checker.CheckName, attempt, maxRetries);
                else
                    _logger.LogInformation("'{Check}' is healthy.", checker.CheckName);
                return;
            }

            _logger.LogWarning("'{Check}' failed on attempt {Attempt}/{Max}: {Message}", checker.CheckName, attempt, maxRetries, result.Message);

            if (attempt < maxRetries)
            {
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (TaskCanceledException)
                {
                    return; // service is stopping
                }
            }
        }

        var finalMessage = $"ALERT: {checker.CheckName} failed after {maxRetries} attempts. Last error: {lastResult?.Message}";
        _logger.LogError(finalMessage);

        await _smsService.NotifyAdminsAsync(Truncate(finalMessage, 300), ct);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
