namespace ExpenseManager.Services;

public interface ITelegramBotClient
{
    Task SendTextMessageAsync(
        string chatId,
        string text,
        string? parseMode = null,
        string? plainTextFallback = null,
        CancellationToken cancellationToken = default);
}
