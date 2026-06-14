namespace ExpenseManager.Services;

public interface ITelegramAdminService
{
    Task<(bool Success, string Message)> RegisterWebhookAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> GetWebhookStatusAsync(CancellationToken cancellationToken = default);
}
