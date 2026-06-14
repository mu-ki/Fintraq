namespace ExpenseManager.Services;

public interface IWhatsAppCloudClient
{
    bool IsConfigured { get; }
    Task SendTextMessageAsync(string phoneNumber, string text, CancellationToken cancellationToken = default);
}
