namespace ExpenseManager.Services;

public interface IWhatsAppCloudClient
{
    Task SendTextMessageAsync(string phoneNumber, string text, CancellationToken cancellationToken = default);
}
