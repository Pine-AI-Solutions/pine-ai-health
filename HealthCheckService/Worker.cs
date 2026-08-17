using HealthCheckService.Services;
using HealthCheckService.Settings;
using Microsoft.Extensions.Options;

namespace HealthCheckService;

public class Worker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<HealthCheckOptions> _healthOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RetryingHealthCheckRunner _runner;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IConfiguration configuration,
        IOptions<HealthCheckOptions> healthOptions,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        RetryingHealthCheckRunner runner,
        ILogger<Worker> logger)
    {
        _configuration = configuration;
        _healthOptions = healthOptions;
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _runner = runner;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        var interval = TimeSpan.FromMinutes(_healthOptions.Value.IntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("=== Starting health check cycle at {Time} ===", DateTimeOffset.Now);

            try
            {
                await RunAllChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during health check cycle.");
            }

            _logger.LogInformation("=== Health check cycle finished. Next run in {Interval}. ===", interval);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // service is stopping - loop condition will exit next iteration
            }
        }
    }

    private async Task RunAllChecksAsync(CancellationToken ct)
    {
        var checkers = BuildCheckers();

        if (checkers.Count == 0)
        {
            _logger.LogWarning("No checks are configured in appsettings.json (SqlServer / IisSites / WindowsServices).");
            return;
        }

        // Each checker manages its own independent retry loop, so we can run them
        // concurrently without one slow/failing check delaying the others.
        var tasks = checkers.Select(checker => _runner.RunAsync(checker, ct));
        await Task.WhenAll(tasks);
    }

    private List<IHealthChecker> BuildCheckers()
    {
        var checkers = new List<IHealthChecker>();

        var sqlOptions = _configuration.GetSection("SqlServer").Get<SqlServerOptions>();
        if (sqlOptions is not null && !string.IsNullOrWhiteSpace(sqlOptions.ConnectionString))
        {
            checkers.Add(new SqlServerHealthChecker(sqlOptions, _loggerFactory.CreateLogger("SqlServerHealthChecker")));
        }

        var sites = _configuration.GetSection("IisSites").Get<List<IisSiteOptions>>() ?? new List<IisSiteOptions>();
        foreach (var site in sites)
        {
            checkers.Add(new IisSiteHealthChecker(site, _httpClientFactory, _loggerFactory.CreateLogger($"IisSiteHealthChecker[{site.Name}]")));
        }

        var services = _configuration.GetSection("WindowsServices").Get<List<WindowsServiceOptions>>() ?? new List<WindowsServiceOptions>();
        foreach (var svc in services)
        {
            checkers.Add(new WindowsServiceHealthChecker(svc, _loggerFactory.CreateLogger($"WindowsServiceHealthChecker[{svc.Name}]")));
        }

        return checkers;
    }
}
