namespace ExpenseManager.Services;

public interface IWhatsAppOptionsProvider
{
    Task<WhatsAppSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}

public sealed class WhatsAppSettings
{
    public string AccessToken { get; init; } = string.Empty;
    public string PhoneNumberId { get; init; } = string.Empty;
    public string VerifyToken { get; init; } = string.Empty;
    public string AppSecret { get; init; } = string.Empty;
    public string WebhookUrl { get; init; } = string.Empty;
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PhoneNumberId);
}
