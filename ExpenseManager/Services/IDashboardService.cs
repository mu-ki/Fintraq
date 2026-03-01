using ExpenseManager.Models;
using ExpenseManager.Models.ViewModels;

namespace ExpenseManager.Services;

public interface IDashboardService
{
    Task<DashboardViewModel> BuildMonthAsync(string userId, int year, int month);
    Task<decimal> GetCurrentBalanceAsync(string userId, Guid accountId);
    Task<decimal> GetBalanceAsOfMonthAsync(string userId, Guid accountId, int year, int month);
    bool IsDueInMonth(TransactionEntry recurring, int year, int month);
    /// <summary>Total scheduled installments from start to end date. Returns null if no end date (ongoing).</summary>
    int? GetTotalScheduledInstallments(TransactionEntry recurring);
}
