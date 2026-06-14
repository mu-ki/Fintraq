using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ExpenseManager.Services;

public sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    ITelegramOptionsProvider optionsProvider,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public async Task SendTextMessageAsync(
        string chatId,
        string text,
        string? parseMode = null,
        string? plainTextFallback = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            logger.LogWarning("Telegram bot token is not configured.");
            return;
        }

        var client = httpClientFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{settings.BotToken}/sendMessage";

        if (!string.IsNullOrWhiteSpace(parseMode))
        {
            var sent = await TrySendAsync(client, url, chatId, text, parseMode, cancellationToken);
            if (sent)
            {
                return;
            }

            logger.LogWarning("Telegram HTML message failed; retrying as plain text.");
        }

        var fallback = plainTextFallback ?? text;
        await TrySendAsync(client, url, chatId, fallback, parseMode: null, cancellationToken);
    }

    private async Task<bool> TrySendAsync(
        HttpClient client,
        string url,
        string chatId,
        string text,
        string? parseMode,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["disable_web_page_preview"] = true
        };

        if (!string.IsNullOrWhiteSpace(parseMode))
        {
            payload["parse_mode"] = parseMode;
        }

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram sendMessage failed: {Status} {Body}", response.StatusCode, body);
        return false;
    }

    internal sealed class TelegramUpdate
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; set; }

        [JsonPropertyName("message")]
        public TelegramMessage? Message { get; set; }
    }

    internal sealed class TelegramMessage
    {
        [JsonPropertyName("message_id")]
        public long MessageId { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("chat")]
        public TelegramChat? Chat { get; set; }
    }

    internal sealed class TelegramChat
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
    }
}
