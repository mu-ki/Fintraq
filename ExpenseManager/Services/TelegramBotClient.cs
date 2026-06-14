using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ExpenseManager.Services;

public sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    ITelegramOptionsProvider optionsProvider,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public async Task SendTextMessageAsync(string chatId, string text, CancellationToken cancellationToken = default)
    {
        var settings = await optionsProvider.GetSettingsAsync(cancellationToken);
        if (!settings.IsConfigured)
        {
            logger.LogWarning("Telegram bot token is not configured.");
            return;
        }

        var client = httpClientFactory.CreateClient();
        var url = $"https://api.telegram.org/bot{settings.BotToken}/sendMessage";
        var payload = new
        {
            chat_id = chatId,
            text,
            disable_web_page_preview = true
        };

        using var response = await client.PostAsJsonAsync(url, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning("Telegram sendMessage failed: {Status} {Body}", response.StatusCode, body);
        }
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
