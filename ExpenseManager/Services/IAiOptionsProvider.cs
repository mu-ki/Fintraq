using ExpenseManager.Models.Ai;

namespace ExpenseManager.Services;

public interface IAiOptionsProvider
{
    Task<AiProvider> GetProviderAsync(CancellationToken cancellationToken = default);
    Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default);
    Task<string> GetModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetApiKeyForProviderAsync(AiProvider provider, CancellationToken cancellationToken = default);
    Task<string> GetModelForProviderAsync(AiProvider provider, CancellationToken cancellationToken = default);
}
