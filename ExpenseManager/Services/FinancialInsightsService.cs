using System.Globalization;
using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.Chat;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class FinancialInsightsService(
    ApplicationDbContext dbContext,
    IDashboardService dashboardService) : IFinancialInsightsService
{
    public Task<FinancialQueryResult> GetBalanceAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken)
    {
        return BuildBalanceResultAsync(userId, year, month, accountName, cancellationToken);
    }

    public Task<FinancialQueryResult> GetIncomeAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken)
    {
        return BuildMonthlyFlowResultAsync(userId, year, month, accountName, TransactionKind.Income, cancellationToken);
    }

    public Task<FinancialQueryResult> GetExpenseAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken)
    {
        return BuildMonthlyFlowResultAsync(userId, year, month, accountName, TransactionKind.Expense, cancellationToken);
    }

    private async Task<FinancialQueryResult> BuildBalanceResultAsync(string userId, int year, int month, string? accountName, CancellationToken cancellationToken)
    {
        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync(cancellationToken);

        var selection = SelectAccounts(accounts, accountName);
        if (selection.requiresClarification)
        {
            return new FinancialQueryResult
            {
                RequiresClarification = true,
                ClarificationQuestion = selection.clarificationQuestion
            };
        }

        var accountItems = new List<AccountAmountItem>();
        foreach (var account in selection.accounts)
        {
            var balance = await dashboardService.GetBalanceAsOfMonthAsync(userId, account.Id, year, month);
            accountItems.Add(new AccountAmountItem
            {
                AccountId = account.Id,
                AccountName = account.AccountName,
                Amount = decimal.Round(balance, 2),
                IsManualOverride = account.UseManualOverride
            });
        }

        return new FinancialQueryResult
        {
            Data = new ChatDataPayload
            {
                Intent = "balance",
                Year = year,
                Month = month,
                MonthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                Accounts = accountItems.OrderByDescending(a => a.Amount).ToList(),
                TotalAmount = decimal.Round(accountItems.Sum(a => a.Amount), 2)
            }
        };
    }

    private async Task<FinancialQueryResult> BuildMonthlyFlowResultAsync(
        string userId,
        int year,
        int month,
        string? accountName,
        TransactionKind kind,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync(cancellationToken);

        var selection = SelectAccounts(accounts, accountName);
        if (selection.requiresClarification)
        {
            return new FinancialQueryResult
            {
                RequiresClarification = true,
                ClarificationQuestion = selection.clarificationQuestion
            };
        }

        var oneTimes = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime &&
                t.Kind == kind &&
                t.Date.HasValue &&
                t.Date.Value >= monthStart &&
                t.Date.Value <= monthEnd)
            .ToListAsync(cancellationToken);

        var recurring = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.Kind == kind &&
                t.IsActive &&
                t.StartDate.HasValue &&
                t.StartDate.Value <= monthEnd &&
                (!t.EndDate.HasValue || t.EndDate.Value >= monthStart))
            .ToListAsync(cancellationToken);

        var dueRecurring = recurring.Where(t => dashboardService.IsDueInMonth(t, year, month)).ToList();

        var completionEntries = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.Date.HasValue &&
                t.Date.Value >= monthStart &&
                t.Date.Value <= monthEnd &&
                t.ParentTransactionId.HasValue)
            .ToListAsync(cancellationToken);

        var completionAmountByParent = completionEntries
            .GroupBy(c => c.ParentTransactionId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CompletedAt ?? x.UpdatedAt).First().Amount);

        decimal EffectiveRecurringAmount(TransactionEntry recurringEntry) =>
            completionAmountByParent.TryGetValue(recurringEntry.Id, out var completedAmount)
                ? completedAmount
                : recurringEntry.Amount;

        Guid? GetAccountId(TransactionEntry entry) =>
            kind == TransactionKind.Income ? entry.ReceivedToAccountId : entry.PaidFromAccountId;

        var selectedAccountIds = selection.accounts.Select(a => a.Id).ToHashSet();
        var accountTotals = new Dictionary<Guid, decimal>();
        var unassignedKey = Guid.Empty;

        void AddAmount(Guid? accountId, decimal amount)
        {
            if (accountName is not null && (!accountId.HasValue || !selectedAccountIds.Contains(accountId.Value)))
            {
                return;
            }

            var key = accountId ?? unassignedKey;
            accountTotals.TryGetValue(key, out var existing);
            accountTotals[key] = existing + amount;
        }

        foreach (var entry in oneTimes)
        {
            AddAmount(GetAccountId(entry), entry.Amount);
        }

        foreach (var entry in dueRecurring)
        {
            AddAmount(GetAccountId(entry), EffectiveRecurringAmount(entry));
        }

        var items = accountTotals
            .Where(kvp => kvp.Value != 0m)
            .Select(kvp => new AccountAmountItem
            {
                AccountId = kvp.Key == unassignedKey ? null : kvp.Key,
                AccountName = kvp.Key != unassignedKey
                    ? selection.accounts.FirstOrDefault(a => a.Id == kvp.Key)?.AccountName
                        ?? accounts.FirstOrDefault(a => a.Id == kvp.Key)?.AccountName
                        ?? "Unknown account"
                    : "Unassigned",
                Amount = decimal.Round(kvp.Value, 2)
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return new FinancialQueryResult
        {
            Data = new ChatDataPayload
            {
                Intent = kind == TransactionKind.Income ? "income" : "expense",
                Year = year,
                Month = month,
                MonthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
                Accounts = items,
                TotalAmount = decimal.Round(items.Sum(a => a.Amount), 2)
            }
        };
    }

    private static (List<BankAccount> accounts, bool requiresClarification, string? clarificationQuestion) SelectAccounts(
        List<BankAccount> accounts,
        string? accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return (accounts, false, null);
        }

        var exact = accounts
            .Where(a => string.Equals(a.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count == 1)
        {
            return (exact, false, null);
        }

        var partial = accounts
            .Where(a => a.AccountName.Contains(accountName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (partial.Count == 1)
        {
            return (partial, false, null);
        }

        if (partial.Count == 0)
        {
            var available = accounts.Count == 0
                ? "No bank accounts found."
                : $"Available accounts: {string.Join(", ", accounts.Select(a => a.AccountName))}.";
            return (new List<BankAccount>(), true, $"I couldn't find account '{accountName}'. {available}");
        }

        return (new List<BankAccount>(), true, $"I found multiple accounts matching '{accountName}': {string.Join(", ", partial.Select(a => a.AccountName))}. Which one do you mean?");
    }
}
