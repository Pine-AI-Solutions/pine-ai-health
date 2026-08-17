using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HealthCheckService.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCheckService.Services;

public interface ISmsNotificationService
{
    /// <summary>Sends the given message to every configured admin phone number.</summary>
    Task NotifyAdminsAsync(string message, CancellationToken ct = default);
}

/// <summary>
/// SMS notifier that talks to the Melipayamak "simple send" API, following the same
/// request shape used in the reference SmsService implementation
/// (POST api/send/simple/{ApiKey} with { from, to, text }).
/// </summary>
public class SmsNotificationService : ISmsNotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SmsOptions _options;
    private readonly ILogger<SmsNotificationService> _logger;

    public SmsNotificationService(IHttpClientFactory httpClientFactory, IOptions<SmsOptions> options, ILogger<SmsNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAdminsAsync(string message, CancellationToken ct = default)
    {
        if (_options.AdminPhones is null || _options.AdminPhones.Count == 0)
        {
            _logger.LogWarning("No admin phone numbers configured in Sms:AdminPhones; skipping SMS for message: {Message}", message);
            return;
        }

        foreach (var phone in _options.AdminPhones)
        {
            await SendAsync(phone, message, ct);
        }
    }

    private async Task SendAsync(string toPhone, string text, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(nameof(SmsNotificationService));
        if (client.BaseAddress is null && !string.IsNullOrWhiteSpace(_options.BaseUrl))
            client.BaseAddress = new Uri(_options.BaseUrl);

        var requestBody = new
        {
            from = _options.ProviderPhone,
            to = toPhone,
            text
        };

        try
        {
            // The Melipayamak API requires the API key as part of the URL path:
            // POST api/send/simple/{API_KEY}
            var httpResponse = await client.PostAsJsonAsync($"api/send/simple/{_options.ApiKey}", requestBody, ct);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "SMS provider returned non-success status {StatusCode} for recipient {ToPhone}. Body: {Body}",
                    (int)httpResponse.StatusCode, toPhone, body);
                return;
            }

            var response = await httpResponse.Content.ReadFromJsonAsync<SmsResponse>(cancellationToken: ct);
            if (response is null)
            {
                _logger.LogWarning("SMS provider returned an empty or unparseable response for recipient {ToPhone}", toPhone);
                return;
            }

            _logger.LogInformation(
                "SMS sent to {ToPhone}: recId={RecId}, status={Status}", toPhone, response.RecId, response.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending SMS to recipient {ToPhone}", toPhone);
        }
    }

    private sealed class SmsResponse
    {
        [JsonPropertyName("recId")]
        public long RecId { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }
    }
}
