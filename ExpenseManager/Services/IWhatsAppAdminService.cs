namespace ExpenseManager.Services;

public interface IWhatsAppAdminService
{
    Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default);
}
