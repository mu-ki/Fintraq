namespace ExpenseManager.Services;

public interface ITelegramBotClient
{
    Task SendTextMessageAsync(string chatId, string text, CancellationToken cancellationToken = default);
}
