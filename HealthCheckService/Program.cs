using HealthCheckService;
using HealthCheckService.Services;
using HealthCheckService.Settings;

var builder = Host.CreateApplicationBuilder(args);

// Run as a real Windows Service when installed via sc.exe / New-Service.
// When run from the console (e.g. "dotnet run" while developing) it just runs normally.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "InfraHealthCheckService";
});

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.Configure<HealthCheckOptions>(builder.Configuration.GetSection("HealthCheck"));
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection("Sms"));

// Named HttpClients: one for probing sites, one for the SMS provider.
builder.Services.AddHttpClient(nameof(IisSiteHealthChecker));
builder.Services.AddHttpClient(nameof(SmsNotificationService));

builder.Services.AddSingleton<ISmsNotificationService, SmsNotificationService>();
builder.Services.AddSingleton<RetryingHealthCheckRunner>();
builder.Services.AddHostedService<Worker>();

// Also log to Windows Event Log so ops can see history without RDP-ing in to read a log file.
// Requires the service account to have permission to write to the "Application" log
// (see README for setup: New-EventLog -LogName Application -Source "InfraHealthCheckService").
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "InfraHealthCheckService";
});

var host = builder.Build();
host.Run();
