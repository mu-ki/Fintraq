using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ExpenseManager.Services;

public sealed class WhatsAppCloudClient(
    IHttpClientFactory httpClientFactory,
    IWhatsAppOptionsProvider optionsProvider,
    ILogger<WhatsAppCloudClient> logger) : IWhatsAppCloudClient
{
    public async Task SendTextMessageAsync(string phoneNumber, string text, CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            logger.LogWarning("WhatsApp Cloud API is not configured.");
            return;
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        var url = $"https://graph.facebook.com/v21.0/{settings.PhoneNumberId}/messages";
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
