using System.ServiceProcess;
using HealthCheckService.Models;
using HealthCheckService.Settings;
using Microsoft.Extensions.Logging;

namespace HealthCheckService.Services;

/// <summary>
/// Checks whether a named Windows service is Running. If it's stopped (or paused),
/// attempts to start/continue it.
/// </summary>
public class WindowsServiceHealthChecker : IHealthChecker
{
    private readonly WindowsServiceOptions _svc;
    private readonly ILogger _logger;

    public string CheckName => $"Windows Service ({_svc.Name})";

    public WindowsServiceHealthChecker(WindowsServiceOptions svc, ILogger logger)
    {
        _svc = svc;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckAndAttemptFixAsync(CancellationToken ct)
    {
        try
        {
            using var controller = new ServiceController(_svc.Name);
            controller.Refresh();

            if (controller.Status == ServiceControllerStatus.Running)
                return Task.FromResult(HealthCheckResult.Ok(CheckName));

            _logger.LogWarning("Service '{Service}' is {Status}. Attempting to start it...", _svc.Name, controller.Status);

            switch (controller.Status)
            {
                case ServiceControllerStatus.Stopped:
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    break;

                case ServiceControllerStatus.Paused:
                    controller.Continue();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    break;

                case ServiceControllerStatus.StartPending:
                case ServiceControllerStatus.ContinuePending:
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    break;

                default:
                    // StopPending / PausePending - wait briefly and re-check on next attempt.
                    break;
            }

            controller.Refresh();
            if (controller.Status == ServiceControllerStatus.Running)
                return Task.FromResult(HealthCheckResult.Ok(CheckName));

            return Task.FromResult(HealthCheckResult.Fail(
                CheckName, $"Service '{_svc.Name}' is still {controller.Status} after attempting to start it."));
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return Task.FromResult(HealthCheckResult.Fail(
                CheckName, $"Timed out waiting for service '{_svc.Name}' to reach the Running state."));
        }
        catch (InvalidOperationException ex)
        {
            // Thrown when the service doesn't exist / can't be queried.
            return Task.FromResult(HealthCheckResult.Fail(
                CheckName, $"Service '{_svc.Name}' not found or inaccessible: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error checking service '{Service}'", _svc.Name);
            return Task.FromResult(HealthCheckResult.Fail(
                CheckName, $"Unexpected error checking service '{_svc.Name}': {ex.Message}"));
        }
    }
}
