using System.Diagnostics;
using HealthCheckService.Models;
using HealthCheckService.Settings;
using Microsoft.Extensions.Logging;

namespace HealthCheckService.Services;

/// <summary>
/// Sends a GET request to a site's URL and expects HTTP 200. If it doesn't get that,
/// it recycles the site's application pool (via appcmd.exe) and checks once more.
/// </summary>
public class IisSiteHealthChecker : IHealthChecker
{
    private readonly IisSiteOptions _site;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public string CheckName => $"IIS Site ({_site.Name})";

    public IisSiteHealthChecker(IisSiteOptions site, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _site = site;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckAndAttemptFixAsync(CancellationToken ct)
    {
        var (ok, info) = await TryGetAsync(ct);
        if (ok)
            return HealthCheckResult.Ok(CheckName);

        _logger.LogWarning(
            "Site '{Site}' is unhealthy ({Info}). Recycling app pool '{Pool}'...",
            _site.Name, info, _site.AppPoolName);

        var recycled = RecycleAppPool(_site.AppPoolName, _logger);
        if (!recycled)
            return HealthCheckResult.Fail(CheckName, $"Site '{_site.Name}' returned '{info}' and recycling app pool '{_site.AppPoolName}' failed.");

        // Give the app pool a moment to spin back up before re-checking.
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        var (ok2, info2) = await TryGetAsync(ct);
        if (ok2)
            return HealthCheckResult.Ok(CheckName);

        return HealthCheckResult.Fail(
            CheckName,
            $"Site '{_site.Name}' still unhealthy ('{info2}') after recycling app pool '{_site.AppPoolName}'.");
    }

    private async Task<(bool Ok, string Info)> TryGetAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(nameof(IisSiteHealthChecker));
            client.Timeout = TimeSpan.FromSeconds(_site.TimeoutSeconds <= 0 ? 15 : _site.TimeoutSeconds);

            using var response = await client.GetAsync(_site.Url, ct);
            var statusCode = (int)response.StatusCode;
            return (statusCode == 200, $"HTTP {statusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Recycles an IIS application pool by shelling out to appcmd.exe. This avoids
    /// requiring a compile-time reference to Microsoft.Web.Administration.dll (which
    /// only exists on machines with IIS Management Tools installed) while still
    /// working reliably as long as IIS Management Console is present on the server.
    /// </summary>
    private static bool RecycleAppPool(string appPoolName, ILogger logger)
    {
        try
        {
            var appCmdPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "inetsrv", "appcmd.exe");

            if (!File.Exists(appCmdPath))
            {
                logger.LogError("appcmd.exe not found at '{Path}'. Is IIS Management Console installed on this server?", appCmdPath);
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = appCmdPath,
                Arguments = $"recycle apppool /apppool.name:\"{appPoolName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(15000);
            var stdErr = process.StandardError.ReadToEnd();

            if (process.ExitCode != 0)
            {
                logger.LogError(
                    "appcmd recycle for pool '{Pool}' exited with code {Code}: {Error}",
                    appPoolName, process.ExitCode, stdErr);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to recycle app pool '{Pool}'", appPoolName);
            return false;
        }
    }
}
