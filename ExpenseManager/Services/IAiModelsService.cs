using ExpenseManager.Models.Ai;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public interface IAiModelsService
{
    Task<IReadOnlyList<string>> ListModelsAsync(AiProvider provider, string apiKey, CancellationToken cancellationToken = default);
}
