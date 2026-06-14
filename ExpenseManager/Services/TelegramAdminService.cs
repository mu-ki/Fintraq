using System.Net.Http.Json;
using System.Text.Json;

namespace ExpenseManager.Services;

public sealed class TelegramAdminService(
    IHttpClientFactory httpClientFactory,
    ITelegramOptionsProvider optionsProvider,
    ILogger<TelegramAdminService> logger) : ITelegramAdminService
{
    public async Task<(bool Success, string Message)> RegisterWebhookAsync(CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return (false, "Bot token is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return (false, "Webhook URL is not configured.");
        }

        var client = httpClientFactory.CreateClient();
        var query = new List<string>
        {
            $"url={Uri.EscapeDataString(settings.WebhookUrl)}"
        };
        if (!string.IsNullOrWhiteSpace(settings.WebhookSecret))
        {
            query.Add($"secret_token={Uri.EscapeDataString(settings.WebhookSecret)}");
        }

        var url = $"https://api.telegram.org/bot{settings.BotToken}/setWebhook?{string.Join("&", query)}";
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Telegram setWebhook failed: {Body}", body);
            return (false, $"Telegram API error: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
        var description = doc.RootElement.TryGetProperty("description", out var descEl)
            ? descEl.GetString()
            : null;
        return ok
            ? (true, description ?? "Webhook registered successfully.")
            : (false, description ?? "Webhook registration failed.");
    }

    public async Task<(bool Success, string Message)> GetWebhookStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            return (false, "Bot token is not configured.");
        }

        var client = httpClientFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{settings.BotToken}/getWebhookInfo";
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, $"Telegram API error: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("result", out var result))
        {
            return (false, body);
        }

        var webhookUrl = result.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : "";
        var pending = result.TryGetProperty("pending_update_count", out var pendingEl) ? pendingEl.GetInt32() : 0;
        var lastError = result.TryGetProperty("last_error_message", out var errEl) ? errEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return (true, "No webhook is registered with Telegram yet.");
        }

        var message = $"Webhook URL: {webhookUrl}. Pending updates: {pending}.";
        if (!string.IsNullOrWhiteSpace(lastError))
        {
            message += $" Last error: {lastError}";
        }

        return (true, message);
    }
}
