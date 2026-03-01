using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public interface IFinancialInsightsService
{
    Task<FinancialQueryResult> GetBalanceAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken);
    Task<FinancialQueryResult> GetIncomeAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken);
    Task<FinancialQueryResult> GetExpenseAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken);
    Task<FinancialQueryResult> GetChitDetailsAsync(string userId, CancellationToken cancellationToken);
}

public sealed class FinancialQueryResult
{
    public bool RequiresClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public ChatDataPayload Data { get; set; } = new();
}
