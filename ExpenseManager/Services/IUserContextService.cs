using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public interface IUserContextService
{
    /// <summary>
    /// Builds full financial context for the current user so the AI can answer any question about their data.
    /// </summary>
    Task<UserFinancialContext> GetContextAsync(string userId, CancellationToken cancellationToken = default);
}
