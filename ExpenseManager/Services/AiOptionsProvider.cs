using ExpenseManager.Configuration;
using ExpenseManager.Models.Ai;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class AiOptionsProvider(
    IOptions<AiOptions> aiOptions,
    IOptions<GeminiOptions> geminiOptions,
    IOptions<AnthropicOptions> anthropicOptions,
    IOptions<CursorAiOptions> cursorOptions,
    IAdminSettingsService adminSettings) : IAiOptionsProvider
{
    private const string KeyProvider = "Ai:Provider";
    private const string KeyGeminiApiKey = "Gemini:ApiKey";
    private const string KeyGeminiModel = "Gemini:Model";
    private const string KeyAnthropicApiKey = "Anthropic:ApiKey";
    private const string KeyAnthropicModel = "Anthropic:Model";
    private const string KeyCursorApiKey = "Cursor:ApiKey";
    private const string KeyCursorModel = "Cursor:Model";

    public async Task<AiProvider> GetProviderAsync(CancellationToken cancellationToken = default)
    {
        var fromDb = await adminSettings.GetAsync(KeyProvider, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromDb) && Enum.TryParse<AiProvider>(fromDb, true, out var parsed))
        {
            return parsed;
        }

        return aiOptions.Value.Provider;
    }

    public async Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(cancellationToken);
        return await GetApiKeyForProviderAsync(provider, cancellationToken);
    }

    public async Task<string> GetModelAsync(CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(cancellationToken);
        return await GetModelForProviderAsync(provider, cancellationToken);
    }

    public async Task<string> GetApiKeyForProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        return provider switch
        {
            AiProvider.Anthropic => await ResolveAsync(KeyAnthropicApiKey, anthropicOptions.Value.ApiKey, cancellationToken),
            AiProvider.Cursor => await ResolveAsync(KeyCursorApiKey, cursorOptions.Value.ApiKey, cancellationToken),
            _ => await ResolveAsync(KeyGeminiApiKey, geminiOptions.Value.ApiKey, cancellationToken)
        };
    }

    public async Task<string> GetModelForProviderAsync(AiProvider provider, CancellationToken cancellationToken = default)
    {
        return provider switch
        {
            AiProvider.Anthropic => await ResolveModelAsync(KeyAnthropicModel, anthropicOptions.Value.Model, "claude-sonnet-4-20250514", cancellationToken),
            AiProvider.Cursor => await ResolveModelAsync(KeyCursorModel, cursorOptions.Value.Model, "composer-2", cancellationToken),
            _ => await ResolveModelAsync(KeyGeminiModel, geminiOptions.Value.Model, "gemini-2.0-flash", cancellationToken)
        };
    }

    private async Task<string> ResolveAsync(string dbKey, string configValue, CancellationToken cancellationToken)
    {
        var fromDb = await adminSettings.GetAsync(dbKey, cancellationToken);
        return !string.IsNullOrWhiteSpace(fromDb) ? fromDb.Trim() : (configValue ?? string.Empty).Trim();
    }

    private async Task<string> ResolveModelAsync(string dbKey, string configValue, string fallback, CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(dbKey, configValue, cancellationToken);
        return string.IsNullOrWhiteSpace(resolved) ? fallback : resolved;
    }
}
