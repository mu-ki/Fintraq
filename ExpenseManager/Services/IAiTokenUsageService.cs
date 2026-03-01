using ExpenseManager.Models;

namespace ExpenseManager.Services;

public interface IAiTokenUsageService
{
    Task RecordAsync(string userId, string model, int promptTokens, int completionTokens, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiTokenUsage>> GetHistoryAsync(int take = 100, int skip = 0, string? userId = null, CancellationToken cancellationToken = default);
    Task<(int TotalPrompt, int TotalCompletion, int TotalCalls)> GetTotalsAsync(DateTime? from = null, DateTime? to = null, string? userId = null, CancellationToken cancellationToken = default);
}
