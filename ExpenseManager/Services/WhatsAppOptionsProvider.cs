using ExpenseManager.Configuration;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class WhatsAppOptionsProvider(
    IOptions<WhatsAppOptions> options,
    IAdminSettingsService adminSettings) : IWhatsAppOptionsProvider
{
    private const string KeyAccessToken = "WhatsApp:AccessToken";
    private const string KeyPhoneNumberId = "WhatsApp:PhoneNumberId";
    private const string KeyVerifyToken = "WhatsApp:VerifyToken";
    private const string KeyAppSecret = "WhatsApp:AppSecret";
    private const string KeyWebhookUrl = "WhatsApp:WebhookUrl";

    public async Task<WhatsAppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var config = options.Value;
        return new WhatsAppSettings
        {
            AccessToken = await ResolveAsync(KeyAccessToken, config.AccessToken, cancellationToken),
            PhoneNumberId = await ResolveAsync(KeyPhoneNumberId, config.PhoneNumberId, cancellationToken),
            VerifyToken = await ResolveAsync(KeyVerifyToken, config.VerifyToken, cancellationToken),
            AppSecret = await ResolveAsync(KeyAppSecret, config.AppSecret, cancellationToken),
            WebhookUrl = await ResolveAsync(KeyWebhookUrl, config.WebhookUrl, cancellationToken)
        };
    }

    private async Task<string> ResolveAsync(string dbKey, string configValue, CancellationToken cancellationToken)
    {
        var fromDb = await adminSettings.GetAsync(dbKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(fromDb) ? fromDb.Trim() : (configValue ?? string.Empty).Trim();
    }
}
