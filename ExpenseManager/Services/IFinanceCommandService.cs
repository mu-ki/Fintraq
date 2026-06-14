using ExpenseManager.Models;
using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public interface IFinanceCommandService
{
    Task<FinanceCommandResult> AddTransactionAsync(string userId, AddTransactionCommand command, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> ListDueItemsAsync(string userId, int? year = null, int? month = null, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> MarkDueDoneAsync(string userId, string titleSearch, int? year = null, int? month = null, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> RevertDueAsync(string userId, string titleSearch, int? year = null, int? month = null, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> AddBankAccountAsync(string userId, string accountName, AccountType accountType, decimal initialBalance, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> ListAccountsAsync(string userId, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> ListCategoriesAsync(string userId, CategoryType? type = null, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> DeleteTransactionAsync(string userId, string titleSearch, CancellationToken cancellationToken = default);
    Task<FinanceCommandResult> GetMonthSummaryAsync(string userId, int? year = null, int? month = null, CancellationToken cancellationToken = default);
}
