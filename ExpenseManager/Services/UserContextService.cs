using System.Globalization;
using System.Text;
using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.Chat;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class UserContextService(
    ApplicationDbContext dbContext,
    IDashboardService dashboardService,
    IFinancialInsightsService financialInsightsService) : IUserContextService
{
    private const int RecentTransactionsCount = 25;
    private const int RecentMonthsForSummary = 3;

    public async Task<UserFinancialContext> GetContextAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var currentYear = now.Year;
        var currentMonth = now.Month;
        var sb = new StringBuilder();

        // Accounts and balances (as of end of current month)
        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync(cancellationToken);

        sb.AppendLine("## Bank accounts and balance (as of end of current month)");
        if (accounts.Count == 0)
        {
            sb.AppendLine("- No accounts.");
        }
        else
        {
            foreach (var acc in accounts)
            {
                var balance = await dashboardService.GetBalanceAsOfMonthAsync(userId, acc.Id, currentYear, currentMonth);
                sb.AppendLine($"- {acc.AccountName}: {balance:0.00}");
            }
        }
        sb.AppendLine();

        // This month and last 2 months summary: income, expense, total balance
        sb.AppendLine("## Monthly summary (income, expense, total balance across all accounts)");
        for (var i = 0; i < RecentMonthsForSummary; i++)
        {
            var d = new DateTime(currentYear, currentMonth, 1).AddMonths(-i);
            var y = d.Year;
            var m = d.Month;
            var incomeResult = await financialInsightsService.GetIncomeAsync(userId, y, m, null, cancellationToken);
            var expenseResult = await financialInsightsService.GetExpenseAsync(userId, y, m, null, cancellationToken);
            var balanceResult = await financialInsightsService.GetBalanceAsync(userId, y, m, null, cancellationToken);
            var monthLabel = new DateTime(y, m, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            sb.AppendLine($"- {monthLabel}: Income {incomeResult.Data.TotalAmount:0.00}, Expense {expenseResult.Data.TotalAmount:0.00}, Total balance {balanceResult.Data.TotalAmount:0.00}");
        }
        sb.AppendLine();

        // Recent transactions (one-time and completion entries, last N by date)
        sb.AppendLine("## Recent transactions (latest first)");
        var recentOneTime = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime &&
                t.Date.HasValue)
            .OrderByDescending(t => t.Date)
            .Take(RecentTransactionsCount)
            .ToListAsync(cancellationToken);

        if (recentOneTime.Count == 0)
        {
            sb.AppendLine("- No one-time transactions.");
        }
        else
        {
            foreach (var t in recentOneTime)
            {
                var kind = t.Kind == TransactionKind.Income ? "Income" : "Expense";
                var cat = t.Category?.Name ?? "—";
                sb.AppendLine($"- {t.Date:dd-MMM-yyyy} | {t.Title} | {kind} | {cat} | {t.Amount:0.00}");
            }
        }
        sb.AppendLine();

        // Recurring items (all active): name, amount, frequency, start/end, and for Chit Fund items: completed/total installments
        sb.AppendLine("## Recurring items (active)");
        var recurring = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.IsActive &&
                t.StartDate.HasValue)
            .OrderBy(t => t.Title)
            .ToListAsync(cancellationToken);

        var chitResult = await financialInsightsService.GetChitDetailsAsync(userId, cancellationToken);
        var chitByTitle = chitResult.Data?.Chits?.ToDictionary(c => c.Title, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, ChitDetailItem>();

        if (recurring.Count == 0)
        {
            sb.AppendLine("- No recurring items.");
        }
        else
        {
            foreach (var r in recurring)
            {
                var freq = r.Frequency?.ToString() ?? "—";
                var start = r.StartDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "—";
                var end = r.EndDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "—";
                var cat = r.Category?.Name ?? "—";
                var line = $"- {r.Title} | {r.Kind} | {cat} | Amount {r.Amount:0.00} | {freq} | Start {start} | End {end}";
                if (chitByTitle.TryGetValue(r.Title, out var chit))
                {
                    var totalStr = chit.TotalInstallments.HasValue ? $"{chit.CompletedCount} of {chit.TotalInstallments} installments" : $"{chit.CompletedCount} completed (ongoing)";
                    line += $" | {totalStr}";
                }
                sb.AppendLine(line);
            }
        }
        sb.AppendLine();

        // Categories (income and expense)
        sb.AppendLine("## Categories");
        var categories = await dbContext.Categories.AsNoTracking().OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var incomeCats = categories.Where(c => c.Type == CategoryType.Income).Select(c => c.Name).ToList();
        var expenseCats = categories.Where(c => c.Type == CategoryType.Expense).Select(c => c.Name).ToList();
        sb.AppendLine($"Income: {string.Join(", ", incomeCats)}");
        sb.AppendLine($"Expense: {string.Join(", ", expenseCats)}");

        return new UserFinancialContext
        {
            ContextForPrompt = sb.ToString().TrimEnd(),
            CurrentMonth = currentMonth,
            CurrentYear = currentYear
        };
    }
}
