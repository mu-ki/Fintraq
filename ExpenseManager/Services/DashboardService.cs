using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace ExpenseManager.Services;

public class DashboardService(ApplicationDbContext dbContext) : IDashboardService
{
    public async Task<DashboardViewModel> BuildMonthAsync(string userId, int year, int month)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var oneTimes = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime &&
                t.Date.HasValue &&
                t.Date.Value >= monthStart &&
                t.Date.Value <= monthEnd)
            .ToListAsync();

        var recurring = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.IsActive &&
                t.StartDate.HasValue &&
                t.StartDate.Value <= monthEnd &&
                (!t.EndDate.HasValue || t.EndDate.Value >= monthStart))
            .ToListAsync();

        var dueRecurring = recurring.Where(t => IsDueInMonth(t, year, month)).ToList();

        var completionEntries = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.Date.HasValue &&
                t.Date.Value >= monthStart &&
                t.Date.Value <= monthEnd &&
                t.ParentTransactionId.HasValue)
            .ToListAsync();

        var completionAmountByParent = completionEntries
            .GroupBy(c => c.ParentTransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CompletedAt ?? x.UpdatedAt).First().Amount);

        decimal EffectiveRecurringAmount(TransactionEntry r) =>
            completionAmountByParent.TryGetValue(r.Id, out var amt) ? amt : r.Amount;

        var totalIncome = oneTimes.Where(t => t.Kind == TransactionKind.Income).Sum(t => t.Amount) +
                          dueRecurring.Where(t => t.Kind == TransactionKind.Income).Sum(EffectiveRecurringAmount);
        var totalExpense = oneTimes.Where(t => t.Kind == TransactionKind.Expense).Sum(t => t.Amount) +
                           dueRecurring.Where(t => t.Kind == TransactionKind.Expense).Sum(EffectiveRecurringAmount);

        var categoryTotals = oneTimes
            .GroupBy(t => new { Name = t.Category?.Name ?? "Uncategorized", t.Kind })
            .Select(g => new CategoryTotalViewModel
            {
                CategoryName = g.Key.Name,
                Kind = g.Key.Kind,
                Total = g.Sum(x => x.Amount)
            })
            .ToList();

        var recurringCategoryTotals = dueRecurring
            .GroupBy(t => new { Name = t.Category?.Name ?? "Uncategorized", t.Kind })
            .Select(g => new CategoryTotalViewModel
            {
                CategoryName = g.Key.Name,
                Kind = g.Key.Kind,
                Total = g.Sum(EffectiveRecurringAmount)
            })
            .ToList();

        foreach (var recurringCategory in recurringCategoryTotals)
        {
            var existingCategory = categoryTotals.FirstOrDefault(c =>
                c.Kind == recurringCategory.Kind &&
                c.CategoryName == recurringCategory.CategoryName);
            if (existingCategory is null)
            {
                categoryTotals.Add(recurringCategory);
            }
            else
            {
                existingCategory.Total += recurringCategory.Total;
            }
        }

        categoryTotals = categoryTotals.OrderByDescending(x => x.Total).ToList();

        var accounts = await dbContext.BankAccounts.Where(a => a.UserId == userId).ToListAsync();
        var bankBalances = new List<BankBalanceViewModel>();
        var yearMonthLabels = Enumerable.Range(1, 12)
            .Select(m => new DateTime(year, m, 1).ToString("MMM", CultureInfo.InvariantCulture))
            .ToList();
        var yearlyAccountBalances = new List<AccountBalanceSeriesViewModel>();

        foreach (var account in accounts)
        {
            var currentBalance = await GetBalanceAsOfMonthAsync(userId, account.Id, year, month);
            bankBalances.Add(new BankBalanceViewModel
            {
                AccountId = account.Id,
                AccountName = account.AccountName,
                CurrentBalance = currentBalance,
                IsManualOverride = account.UseManualOverride
            });

            var monthlyBalances = new List<decimal>(12);
            for (var m = 1; m <= 12; m++)
            {
                monthlyBalances.Add(await GetBalanceAsOfMonthAsync(userId, account.Id, year, m));
            }

            yearlyAccountBalances.Add(new AccountBalanceSeriesViewModel
            {
                AccountName = account.AccountName,
                MonthlyBalances = monthlyBalances
            });
        }

        var selectedMonthStart = new DateOnly(year, month, 1);

        var oneTimeDueItems = oneTimes
            .Select(t =>
            {
                return new RecurringDueItemViewModel
                {
                    TransactionId = t.Id,
                    Title = t.Title,
                    Amount = t.Amount,
                    Kind = t.Kind,
                    IsCompleted = t.IsCompleted,
                    IsRecurring = false,
                    DueDate = t.Date
                };
            });

        var recurringDueItems = dueRecurring
            .Select(t => new RecurringDueItemViewModel
            {
                TransactionId = t.Id,
                Title = t.Title,
                Amount = EffectiveRecurringAmount(t),
                Kind = t.Kind,
                IsCompleted = completionAmountByParent.ContainsKey(t.Id),
                IsRecurring = true,
                DueDate = selectedMonthStart
            });

        var dueItems = recurringDueItems.Concat(oneTimeDueItems).OrderBy(t => t.Kind).ThenBy(t => t.Title).ToList();
        return new DashboardViewModel
        {
            Year = year,
            Month = month,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            BankBalances = bankBalances,
            YearMonthLabels = yearMonthLabels,
            YearlyAccountBalances = yearlyAccountBalances,
            CategoryTotals = categoryTotals,
            CompletedRecurringCount = dueRecurring.Count(t => completionAmountByParent.ContainsKey(t.Id)),
            PendingRecurringCount = dueRecurring.Count(t => !completionAmountByParent.ContainsKey(t.Id)),
            TopExpenseCategory = categoryTotals
                .Where(c => c.Kind == TransactionKind.Expense)
                .OrderByDescending(c => c.Total)
                .Select(c => c.CategoryName)
                .FirstOrDefault() ?? "N/A",
            HighestDueExpense = dueRecurring
                .Where(t => t.Kind == TransactionKind.Expense)
                .Select(EffectiveRecurringAmount)
                .DefaultIfEmpty(0m)
                .Max(),
            RecurringDueItems = dueItems
        };
    }

    public async Task<decimal> GetCurrentBalanceAsync(string userId, Guid accountId)
    {
        var now = DateTime.Now;
        return await GetBalanceAsOfMonthAsync(userId, accountId, now.Year, now.Month);
    }

    public async Task<decimal> GetBalanceAsOfMonthAsync(string userId, Guid accountId, int year, int month)
    {
        var account = await dbContext.BankAccounts
            .Where(a => a.UserId == userId && a.Id == accountId)
            .SingleAsync();

        if (account.UseManualOverride && account.ManualBalanceOverride.HasValue)
        {
            return account.ManualBalanceOverride.Value;
        }

        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var oneTimeCredits = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime &&
                t.Kind == TransactionKind.Income &&
                t.ReceivedToAccountId == accountId &&
                t.Date.HasValue &&
                t.Date.Value <= monthEnd)
            .SumAsync(t => t.Amount);

        var oneTimeDebits = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime &&
                t.Kind == TransactionKind.Expense &&
                t.PaidFromAccountId == accountId &&
                t.Date.HasValue &&
                t.Date.Value <= monthEnd)
            .SumAsync(t => t.Amount);

        var recurringCredits = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.Kind == TransactionKind.Income &&
                t.ReceivedToAccountId == accountId &&
                t.StartDate.HasValue &&
                t.StartDate.Value <= monthEnd)
            .ToListAsync();

        var recurringDebits = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.Kind == TransactionKind.Expense &&
                t.PaidFromAccountId == accountId &&
                t.StartDate.HasValue &&
                t.StartDate.Value <= monthEnd)
            .ToListAsync();

        var recurringCreditTotal = recurringCredits.Sum(t => t.Amount * CountOccurrencesUntil(t, monthEnd));
        var recurringDebitTotal = recurringDebits.Sum(t => t.Amount * CountOccurrencesUntil(t, monthEnd));

        return account.InitialBalance + oneTimeCredits + recurringCreditTotal - oneTimeDebits - recurringDebitTotal;
    }

    public bool IsDueInMonth(TransactionEntry recurring, int year, int month)
    {
        if (!recurring.StartDate.HasValue || recurring.Frequency is null)
        {
            return false;
        }

        var start = recurring.StartDate.Value;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        if (monthEnd < start)
        {
            return false;
        }

        if (recurring.EndDate.HasValue && monthStart > recurring.EndDate.Value)
        {
            return false;
        }

        return recurring.Frequency.Value switch
        {
            RecurrenceFrequency.Weekly => WeeklyHasOccurrence(start, recurring.EndDate, monthStart, monthEnd),
            RecurrenceFrequency.Monthly => MonthOffsetMatch(start, monthStart, 1),
            RecurrenceFrequency.Quarterly => MonthOffsetMatch(start, monthStart, 3),
            RecurrenceFrequency.Every4Months => MonthOffsetMatch(start, monthStart, 4),
            RecurrenceFrequency.HalfYearly => MonthOffsetMatch(start, monthStart, 6),
            RecurrenceFrequency.Yearly => MonthOffsetMatch(start, monthStart, 12),
            _ => false
        };
    }

    private static bool MonthOffsetMatch(DateOnly start, DateOnly monthStart, int interval)
    {
        var diff = ((monthStart.Year - start.Year) * 12) + monthStart.Month - start.Month;
        return diff >= 0 && diff % interval == 0;
    }

    private static bool WeeklyHasOccurrence(DateOnly start, DateOnly? end, DateOnly monthStart, DateOnly monthEnd)
    {
        var probe = start;
        if (probe < monthStart)
        {
            var days = monthStart.DayNumber - start.DayNumber;
            var jumps = days / 7;
            probe = start.AddDays(jumps * 7);
            while (probe < monthStart)
            {
                probe = probe.AddDays(7);
            }
        }

        if (probe > monthEnd)
        {
            return false;
        }

        if (end.HasValue && probe > end.Value)
        {
            return false;
        }

        return true;
    }

    private static int CountOccurrencesUntil(TransactionEntry recurring, DateOnly monthEnd)
    {
        if (!recurring.StartDate.HasValue || recurring.Frequency is null)
        {
            return 0;
        }

        var start = recurring.StartDate.Value;
        var effectiveEnd = recurring.EndDate.HasValue && recurring.EndDate.Value < monthEnd
            ? recurring.EndDate.Value
            : monthEnd;

        if (effectiveEnd < start)
        {
            return 0;
        }

        return recurring.Frequency.Value switch
        {
            RecurrenceFrequency.Weekly => ((effectiveEnd.DayNumber - start.DayNumber) / 7) + 1,
            RecurrenceFrequency.Monthly => CountMonthlyByInterval(start, effectiveEnd, 1),
            RecurrenceFrequency.Quarterly => CountMonthlyByInterval(start, effectiveEnd, 3),
            RecurrenceFrequency.Every4Months => CountMonthlyByInterval(start, effectiveEnd, 4),
            RecurrenceFrequency.HalfYearly => CountMonthlyByInterval(start, effectiveEnd, 6),
            RecurrenceFrequency.Yearly => CountMonthlyByInterval(start, effectiveEnd, 12),
            _ => 0
        };
    }

    private static int CountMonthlyByInterval(DateOnly start, DateOnly end, int interval)
    {
        var diffMonths = ((end.Year - start.Year) * 12) + end.Month - start.Month;
        if (diffMonths < 0)
        {
            return 0;
        }

        return (diffMonths / interval) + 1;
    }

    public int? GetTotalScheduledInstallments(TransactionEntry recurring)
    {
        if (!recurring.EndDate.HasValue || !recurring.StartDate.HasValue || recurring.Frequency is null)
        {
            return null;
        }
        return CountOccurrencesUntil(recurring, recurring.EndDate.Value);
    }
}


