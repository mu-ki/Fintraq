namespace ExpenseManager.Services;

public interface IGeminiOptionsProvider
{
    Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default);
    Task<string> GetModelAsync(CancellationToken cancellationToken = default);
}
