using ExpenseManager.Configuration;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class TelegramOptionsProvider(
    IOptions<TelegramOptions> options,
    IAdminSettingsService adminSettings) : ITelegramOptionsProvider
{
    private const string KeyBotToken = "Telegram:BotToken";
    private const string KeyWebhookSecret = "Telegram:WebhookSecret";
    private const string KeyBotUsername = "Telegram:BotUsername";
    private const string KeyWebhookUrl = "Telegram:WebhookUrl";

    public async Task<TelegramSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        return new TelegramSettings
        {
            BotToken = await ResolveAsync(KeyBotToken, config.BotToken, cancellationToken),
            WebhookSecret = await ResolveAsync(KeyWebhookSecret, config.WebhookSecret, cancellationToken),
            BotUsername = await ResolveAsync(KeyBotUsername, config.BotUsername, cancellationToken),
            WebhookUrl = await ResolveAsync(KeyWebhookUrl, config.WebhookUrl, cancellationToken)
        };
    }

    private async Task<string> ResolveAsync(string dbKey, string configValue, CancellationToken cancellationToken)
    {
        var fromDb = await adminSettings.GetAsync(dbKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(fromDb) ? fromDb.Trim() : (configValue ?? string.Empty).Trim();
    }
}
