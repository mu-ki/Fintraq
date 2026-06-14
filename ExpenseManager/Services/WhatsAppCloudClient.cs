using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExpenseManager.Configuration;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class WhatsAppCloudClient(
    IHttpClientFactory httpClientFactory,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppCloudClient> logger) : IWhatsAppCloudClient
{
    private readonly WhatsAppOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.AccessToken) &&
        !string.IsNullOrWhiteSpace(_options.PhoneNumberId);

    public async Task SendTextMessageAsync(string phoneNumber, string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("WhatsApp Cloud API is not configured.");
            return;
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);

        var url = $"https://graph.facebook.com/v21.0/{_options.PhoneNumberId}/messages";
        var payload = new
        {
            messaging_product = "whatsapp",
            to = phoneNumber,
            type = "text",
            text = new { body = text }
        };

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("WhatsApp send message failed: {Status} {Body}", response.StatusCode, body);
        }
    }
}
