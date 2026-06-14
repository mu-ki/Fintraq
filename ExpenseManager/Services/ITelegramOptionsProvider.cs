namespace ExpenseManager.Services;

public interface ITelegramOptionsProvider
{
    Task<TelegramSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}

public sealed class TelegramSettings
{
    public string BotToken { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string BotUsername { get; init; } = string.Empty;
    public string WebhookUrl { get; init; } = string.Empty;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BotToken);
}
