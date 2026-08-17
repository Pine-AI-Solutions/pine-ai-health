namespace HealthCheckService.Models;

public class HealthCheckResult
{
    public bool Success { get; init; }
    public string CheckName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public static HealthCheckResult Ok(string checkName) =>
        new() { Success = true, CheckName = checkName, Message = "OK" };

    public static HealthCheckResult Fail(string checkName, string message) =>
        new() { Success = false, CheckName = checkName, Message = message };
}
