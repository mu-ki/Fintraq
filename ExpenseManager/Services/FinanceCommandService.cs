using System.Globalization;
using System.Text;
using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.Messaging;
using ExpenseManager.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class FinanceCommandService(
    ApplicationDbContext dbContext,
    IDashboardService dashboardService) : IFinanceCommandService
{
    public async Task<FinanceCommandResult> AddTransactionAsync(string userId, AddTransactionCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Amount <= 0)
        {
            return Fail("Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Fail("Title is required.");
        }

        var category = await ResolveCategoryAsync(command.CategoryName, command.Kind, cancellationToken);
        if (category is null)
        {
            return Fail($"Category not found: {command.CategoryName ?? "(none)"}. Use list_categories to see available categories.");
        }

        Guid? paidFromAccountId = null;
        Guid? receivedToAccountId = null;

        if (command.Kind == TransactionKind.Expense)
        {
            if (string.IsNullOrWhiteSpace(command.AccountName))
            {
                return Fail("Paid From Account is required for expenses.");
            }

            var account = await ResolveAccountAsync(userId, command.AccountName, cancellationToken);
            if (account is null)
            {
                return Fail($"Account not found: {command.AccountName}. Use list_accounts to see your accounts.");
            }

            paidFromAccountId = account.Id;
        }
        else if (!string.IsNullOrWhiteSpace(command.AccountName))
        {
            var account = await ResolveAccountAsync(userId, command.AccountName, cancellationToken);
            if (account is null)
            {
                return Fail($"Account not found: {command.AccountName}.");
            }

            receivedToAccountId = account.Id;
        }

        if (command.ScheduleType == ScheduleType.OneTime && !command.Date.HasValue)
        {
            command.Date = DateOnly.FromDateTime(DateTime.Now);
        }

        if (command.ScheduleType == ScheduleType.Recurring)
        {
            if (!command.StartDate.HasValue)
            {
                command.StartDate = DateOnly.FromDateTime(DateTime.Now);
            }

            if (!command.Frequency.HasValue)
            {
                return Fail("Frequency is required for recurring transactions.");
            }
        }

        var entity = new TransactionEntry
        {
            UserId = userId,
            Title = command.Title.Trim(),
            Amount = command.Amount,
            CategoryId = category.Id,
            Kind = command.Kind,
            ScheduleType = command.ScheduleType,
            Frequency = command.ScheduleType == ScheduleType.Recurring ? command.Frequency : null,
            Date = command.ScheduleType == ScheduleType.OneTime ? command.Date : null,
            StartDate = command.ScheduleType == ScheduleType.Recurring ? command.StartDate : null,
            EndDate = command.ScheduleType == ScheduleType.Recurring ? command.EndDate : null,
            PaidFromAccountId = paidFromAccountId,
            ReceivedToAccountId = receivedToAccountId,
            RecurrenceGroupId = command.ScheduleType == ScheduleType.Recurring ? Guid.NewGuid() : null,
            IsCompleted = false
        };

        dbContext.Transactions.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var kindLabel = command.Kind == TransactionKind.Income ? "income" : "expense";
        return Ok($"Logged {FormatMoney(command.Amount)} {category.Name} {kindLabel}: {entity.Title}.", entity.Id);
    }

    public async Task<FinanceCommandResult> ListDueItemsAsync(string userId, int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        var dashboard = await dashboardService.BuildMonthAsync(userId, y, m);
        var pending = dashboard.RecurringDueItems.Where(i => !i.IsCompleted).ToList();

        if (pending.Count == 0)
        {
            return Ok($"No pending due items for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{pending.Count} due item(s) for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}:");
        foreach (var item in pending)
        {
            var type = item.IsRecurring ? "recurring" : "one-time";
            sb.AppendLine($"• {item.Title} {FormatMoney(item.Amount)} ({type})");
        }

        return Ok(sb.ToString().TrimEnd());
    }

    public async Task<FinanceCommandResult> MarkDueDoneAsync(string userId, string titleSearch, int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        var dashboard = await dashboardService.BuildMonthAsync(userId, y, m);
        var match = FindDueItem(dashboard.RecurringDueItems, titleSearch);
        if (match is null)
        {
            return Fail($"No pending due item matching \"{titleSearch}\" for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}.");
        }

        if (match.IsCompleted)
        {
            return Fail($"\"{match.Title}\" is already marked done.");
        }

        if (match.IsRecurring)
        {
            return await MarkRecurringCompletedAsync(userId, match.TransactionId, y, m, match.Amount, match.Title, cancellationToken);
        }

        return await MarkOneTimeCompletedAsync(userId, match.TransactionId, y, m, match.Title, cancellationToken);
    }

    public async Task<FinanceCommandResult> RevertDueAsync(string userId, string titleSearch, int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        var monthDate = new DateOnly(y, m, 1);

        var dashboard = await dashboardService.BuildMonthAsync(userId, y, m);
        var match = dashboard.RecurringDueItems
            .FirstOrDefault(i => i.IsCompleted && i.Title.Contains(titleSearch, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return Fail($"No completed due item matching \"{titleSearch}\" for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}.");
        }

        if (match.IsRecurring)
        {
            var completion = await dbContext.Transactions
                .Where(t =>
                    t.UserId == userId &&
                    t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                    t.ParentTransactionId == match.TransactionId &&
                    t.Date == monthDate)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (completion is null)
            {
                return Fail($"Could not revert \"{match.Title}\".");
            }

            completion.IsDeleted = true;
            completion.UpdatedAt = DateTime.Now;
        }
        else
        {
            var transaction = await dbContext.Transactions
                .SingleOrDefaultAsync(t =>
                    t.Id == match.TransactionId &&
                    t.UserId == userId &&
                    t.EntryRole == TransactionEntryRole.Standard &&
                    t.ScheduleType == ScheduleType.OneTime, cancellationToken);

            if (transaction is null)
            {
                return Fail($"Could not revert \"{match.Title}\".");
            }

            transaction.IsCompleted = false;
            transaction.CompletedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok($"Reverted \"{match.Title}\" for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}.");
    }

    public async Task<FinanceCommandResult> AddBankAccountAsync(string userId, string accountName, AccountType accountType, decimal initialBalance, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return Fail("Account name is required.");
        }

        var exists = await dbContext.BankAccounts
            .AnyAsync(a => a.UserId == userId && a.AccountName == accountName.Trim(), cancellationToken);

        if (exists)
        {
            return Fail($"Account \"{accountName.Trim()}\" already exists.");
        }

        var account = new BankAccount
        {
            UserId = userId,
            AccountName = accountName.Trim(),
            AccountType = accountType,
            InitialBalance = initialBalance
        };

        dbContext.BankAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok($"Created account \"{account.AccountName}\" with initial balance {FormatMoney(initialBalance)}.");
    }

    public async Task<FinanceCommandResult> ListAccountsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync(cancellationToken);

        if (accounts.Count == 0)
        {
            return Ok("No bank accounts found.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Your accounts:");
        foreach (var account in accounts)
        {
            var balance = await dashboardService.GetCurrentBalanceAsync(userId, account.Id);
            sb.AppendLine($"• {account.AccountName}: {FormatMoney(balance)}");
        }

        return Ok(sb.ToString().TrimEnd());
    }

    public async Task<FinanceCommandResult> ListCategoriesAsync(string userId, CategoryType? type = null, CancellationToken cancellationToken = default)
    {
        _ = userId;
        var query = dbContext.Categories.AsQueryable();
        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        var categories = await query.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        if (categories.Count == 0)
        {
            return Ok("No categories found.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("Categories:");
        foreach (var group in categories.GroupBy(c => c.Type))
        {
            sb.AppendLine($"{group.Key}: {string.Join(", ", group.Select(c => c.Name))}");
        }

        return Ok(sb.ToString().TrimEnd());
    }

    public async Task<FinanceCommandResult> DeleteTransactionAsync(string userId, string titleSearch, CancellationToken cancellationToken = default)
    {
        var transaction = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.Title.Contains(titleSearch))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (transaction is null)
        {
            return Fail($"No transaction matching \"{titleSearch}\".");
        }

        var now = DateTime.Now;
        if (transaction.ScheduleType == ScheduleType.Recurring)
        {
            IQueryable<TransactionEntry> recurringSeriesQuery = dbContext.Transactions.Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.Recurring);

            if (transaction.RecurrenceGroupId.HasValue)
            {
                var recurrenceGroupId = transaction.RecurrenceGroupId.Value;
                recurringSeriesQuery = recurringSeriesQuery.Where(t => t.RecurrenceGroupId == recurrenceGroupId);
            }
            else
            {
                recurringSeriesQuery = recurringSeriesQuery.Where(t => t.Id == transaction.Id);
            }

            var recurringSeries = await recurringSeriesQuery.ToListAsync(cancellationToken);
            var recurringIds = recurringSeries.Select(t => t.Id).ToList();

            foreach (var recurring in recurringSeries)
            {
                recurring.IsDeleted = true;
                recurring.IsActive = false;
                recurring.UpdatedAt = now;
            }

            if (recurringIds.Count > 0)
            {
                var completionEntries = await dbContext.Transactions
                    .Where(t =>
                        t.UserId == userId &&
                        t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                        t.ParentTransactionId.HasValue &&
                        recurringIds.Contains(t.ParentTransactionId.Value))
                    .ToListAsync(cancellationToken);

                foreach (var completion in completionEntries)
                {
                    completion.IsDeleted = true;
                    completion.UpdatedAt = now;
                }
            }
        }
        else
        {
            transaction.IsDeleted = true;
            transaction.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok($"Deleted transaction \"{transaction.Title}\".");
    }

    public async Task<FinanceCommandResult> GetMonthSummaryAsync(string userId, int? year = null, int? month = null, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var now = DateTime.Now;
        var y = year ?? now.Year;
        var m = month ?? now.Month;

        var dashboard = await dashboardService.BuildMonthAsync(userId, y, m);
        var monthLabel = $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m)} {y}";
        var totalBalance = dashboard.BankBalances.Sum(b => b.CurrentBalance);

        var sb = new StringBuilder();
        sb.AppendLine($"{monthLabel} summary:");
        sb.AppendLine($"Income: {FormatMoney(dashboard.TotalIncome)}");
        sb.AppendLine($"Expense: {FormatMoney(dashboard.TotalExpense)}");
        sb.AppendLine($"Net: {FormatMoney(dashboard.NetBalance)}");
        sb.AppendLine($"Total balance: {FormatMoney(totalBalance)}");

        if (dashboard.BankBalances.Count > 0)
        {
            sb.AppendLine("Accounts:");
            foreach (var account in dashboard.BankBalances)
            {
                sb.AppendLine($"• {account.AccountName}: {FormatMoney(account.CurrentBalance)}");
            }
        }

        return Ok(sb.ToString().TrimEnd());
    }

    private async Task<FinanceCommandResult> MarkRecurringCompletedAsync(
        string userId,
        Guid recurringId,
        int year,
        int month,
        decimal amount,
        string title,
        CancellationToken cancellationToken)
    {
        var recurring = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == recurringId &&
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard, cancellationToken);

        if (recurring is null || !dashboardService.IsDueInMonth(recurring, year, month))
        {
            return Fail("This recurring item is not due in the selected month.");
        }

        var monthDate = new DateOnly(year, month, 1);
        var existing = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.ParentTransactionId == recurring.Id &&
                t.Date == monthDate)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            dbContext.Transactions.Add(new TransactionEntry
            {
                UserId = userId,
                Title = recurring.Title,
                Amount = amount,
                Kind = recurring.Kind,
                ScheduleType = ScheduleType.OneTime,
                Date = monthDate,
                CategoryId = recurring.CategoryId,
                PaidFromAccountId = recurring.PaidFromAccountId,
                ReceivedToAccountId = recurring.ReceivedToAccountId,
                ParentTransactionId = recurring.Id,
                RecurrenceGroupId = recurring.RecurrenceGroupId,
                EntryRole = TransactionEntryRole.RecurringCompletion,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            });
        }
        else
        {
            existing.Amount = amount;
            existing.CompletedAt = DateTime.Now;
            existing.IsCompleted = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok($"Marked \"{title}\" {FormatMoney(amount)} done for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {year}.");
    }

    private async Task<FinanceCommandResult> MarkOneTimeCompletedAsync(
        string userId,
        Guid transactionId,
        int year,
        int month,
        string title,
        CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == transactionId &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.ScheduleType == ScheduleType.OneTime, cancellationToken);

        if (transaction is null ||
            !transaction.Date.HasValue ||
            transaction.Date.Value.Year != year ||
            transaction.Date.Value.Month != month)
        {
            return Fail("This one-time item is not part of the selected month.");
        }

        transaction.IsCompleted = true;
        transaction.CompletedAt = DateTime.Now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok($"Marked \"{title}\" done for {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {year}.");
    }

    private async Task<Category?> ResolveCategoryAsync(string? categoryName, TransactionKind kind, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            var defaultType = kind == TransactionKind.Income ? CategoryType.Income : CategoryType.Expense;
            return await dbContext.Categories
                .Where(c => c.Type == defaultType)
                .OrderBy(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var normalized = categoryName.Trim();
        return await dbContext.Categories
            .Where(c => c.Name.Contains(normalized))
            .OrderBy(c => c.Name.Length)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<BankAccount?> ResolveAccountAsync(string userId, string accountName, CancellationToken cancellationToken)
    {
        var normalized = accountName.Trim();
        return await dbContext.BankAccounts
            .Where(a => a.UserId == userId && a.AccountName.Contains(normalized))
            .OrderBy(a => a.AccountName.Length)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static RecurringDueItemViewModel? FindDueItem(IEnumerable<RecurringDueItemViewModel> items, string titleSearch)
    {
        return items
            .Where(i => !i.IsCompleted)
            .FirstOrDefault(i => i.Title.Contains(titleSearch, StringComparison.OrdinalIgnoreCase));
    }

    private static FinanceCommandResult Ok(string message, Guid? transactionId = null) =>
        new() { Success = true, Message = message, TransactionId = transactionId };

    private static FinanceCommandResult Fail(string message) =>
        new() { Success = false, Message = message, RequiresClarification = true };

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", CultureInfo.GetCultureInfo("en-IN"));
}
