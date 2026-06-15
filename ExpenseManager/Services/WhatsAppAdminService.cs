using System.Net.Http.Headers;
using System.Text.Json;

namespace ExpenseManager.Services;

public sealed class WhatsAppAdminService(
    IHttpClientFactory httpClientFactory,
    IWhatsAppOptionsProvider optionsProvider,
    ILogger<WhatsAppAdminService> logger) : IWhatsAppAdminService
{
    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return (false, "Access token and phone number ID are required.");
        }

        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        var url =
            $"https://graph.facebook.com/v21.0/{settings.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating";
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("WhatsApp connection test failed: {Body}", body);
            return (false, $"Meta API error: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var displayNumber = root.TryGetProperty("display_phone_number", out var numberEl)
            ? numberEl.GetString()
            : null;
        var verifiedName = root.TryGetProperty("verified_name", out var nameEl)
            ? nameEl.GetString()
            : null;
        var quality = root.TryGetProperty("quality_rating", out var qualityEl)
            ? qualityEl.GetString()
            : null;

        var parts = new List<string> { "Connected to Meta Cloud API." };
        if (!string.IsNullOrWhiteSpace(verifiedName))
        {
            parts.Add($"Business: {verifiedName}.");
        }

        if (!string.IsNullOrWhiteSpace(displayNumber))
        {
            parts.Add($"Number: {displayNumber}.");
        }

        if (!string.IsNullOrWhiteSpace(quality))
        {
            parts.Add($"Quality: {quality}.");
        }

        return (true, string.Join(" ", parts));
    }
}
