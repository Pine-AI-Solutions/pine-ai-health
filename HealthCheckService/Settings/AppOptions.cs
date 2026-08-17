namespace HealthCheckService.Settings;

/// <summary>
/// Controls how often the whole check cycle runs and how retries behave.
/// </summary>
public class HealthCheckOptions
{
    public int IntervalMinutes { get; set; } = 10;
    public int MaxRetries { get; set; } = 5;
    public int RetryDelayMinutes { get; set; } = 1;
}

/// <summary>
/// Connection info for the SQL Server instance/database to check.
/// </summary>
public class SqlServerOptions
{
    public string Name { get; set; } = "SqlServer";
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// One IIS-hosted site to probe over HTTP, and the app pool to recycle if it's unhealthy.
/// </summary>
public class IisSiteOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>
/// One Windows service that should be running.
/// </summary>
public class WindowsServiceOptions
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Settings for the SMS provider (Melipayamak "simple send" API), plus who gets notified.
/// </summary>
public class SmsOptions
{
    public string BaseUrl { get; set; } = "https://console.melipayamak.com/";
    public string ApiKey { get; set; } = string.Empty;
    public string ProviderPhone { get; set; } = string.Empty;
    public List<string> AdminPhones { get; set; } = new();
}
