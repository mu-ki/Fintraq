using ExpenseManager.Configuration;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class GeminiOptionsProvider(
    IOptions<GeminiOptions> options,
    IAdminSettingsService adminSettings) : IGeminiOptionsProvider
{
    private const string KeyApiKey = "Gemini:ApiKey";
    private const string KeyModel = "Gemini:Model";

    public async Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        var fromDb = await adminSettings.GetAsync(KeyApiKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(fromDb) ? fromDb.Trim() : (options.Value.ApiKey ?? "").Trim();
    }

    public async Task<string> GetModelAsync(CancellationToken cancellationToken = default)
    {
        var fromDb = await adminSettings.GetAsync(KeyModel, cancellationToken);
        return !string.IsNullOrWhiteSpace(fromDb) ? fromDb.Trim() : (options.Value.Model ?? "gemini-2.0-flash").Trim();
    }
}
