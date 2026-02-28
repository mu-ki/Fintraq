using ExpenseManager.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseManager.Data;

/// <summary>Demo account used for seeding and displayed on the login page.</summary>
public static class DemoAccount
{
    public const string Email = "demo@expensemanager.local";
    public const string Password = "Demo@12345";
}

public static class SeedData
{
    private const string DemoEmail = DemoAccount.Email;
    private const string DemoPassword = DemoAccount.Password;

    public static async Task SeedCategoriesAsync(ApplicationDbContext dbContext)
    {
        if (dbContext.Categories.Any())
        {
            return;
        }

        var income = new[] { "Salary", "Business", "Investments", "Other" };
        var expense = new[] { "Food", "Travel", "EMI", "Chit Fund", "Utilities", "Insurance", "Shopping" };

        dbContext.Categories.AddRange(income.Select(name => new Category
        {
            Name = name,
            Type = CategoryType.Income,
            IsSystem = true
        }));

        dbContext.Categories.AddRange(expense.Select(name => new Category
        {
            Name = name,
            Type = CategoryType.Expense,
            IsSystem = true
        }));

        await dbContext.SaveChangesAsync();
    }

    public static async Task SeedDemoUserAsync(UserManager<IdentityUser> userManager)
    {
        var existingUser = await userManager.FindByEmailAsync(DemoEmail);
        if (existingUser is not null)
        {
            return;
        }

        var user = new IdentityUser
        {
            UserName = DemoEmail,
            Email = DemoEmail,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, DemoPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Unable to seed demo user: {errors}");
        }

        await userManager.AddClaimAsync(user, new Claim("display_name", "Demo User"));
    }

    public static async Task<bool> IsInitialSetupRunAsync(ApplicationDbContext dbContext)
    {
        var hasDemoUser = await dbContext.Users.AnyAsync(u => u.Email == DemoEmail);
        var hasTransactions = await dbContext.Transactions.IgnoreQueryFilters().AnyAsync();
        var hasBankAccounts = await dbContext.BankAccounts.IgnoreQueryFilters().AnyAsync();

        return !hasDemoUser && !hasTransactions && !hasBankAccounts;
    }

    public static async Task SeedDemoFinancialDataAsync(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager)
    {
        var demoUser = await userManager.FindByEmailAsync(DemoEmail);
        if (demoUser is null)
        {
            return;
        }

        var userId = demoUser.Id;
        var alreadySeeded = await dbContext.Transactions.AnyAsync(t => t.UserId == userId) ||
                            await dbContext.BankAccounts.AnyAsync(a => a.UserId == userId);
        if (alreadySeeded)
        {
            return;
        }

        var salaryCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Income, "Salary");
        var businessCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Income, "Business");
        var foodCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Expense, "Food");
        var emiCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Expense, "EMI");
        var utilitiesCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Expense, "Utilities");
        var chitFundCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Expense, "Chit Fund");
        var shoppingCategoryId = await GetCategoryIdAsync(dbContext, CategoryType.Expense, "Shopping");

        var salaryAccount = new BankAccount
        {
            UserId = userId,
            AccountName = "Primary Salary Account",
            AccountType = AccountType.Salary,
            InitialBalance = 25000m
        };

        var savingsAccount = new BankAccount
        {
            UserId = userId,
            AccountName = "Rainy Day Savings",
            AccountType = AccountType.Savings,
            InitialBalance = 60000m
        };

        dbContext.BankAccounts.AddRange(salaryAccount, savingsAccount);
        await dbContext.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var threeMonthsAgo = monthStart.AddMonths(-3);

        var salaryRecurring = new TransactionEntry
        {
            UserId = userId,
            Title = "Monthly Salary",
            Amount = 85000m,
            Kind = TransactionKind.Income,
            ScheduleType = ScheduleType.Recurring,
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = threeMonthsAgo,
            CategoryId = salaryCategoryId,
            ReceivedToAccountId = salaryAccount.Id,
            IsActive = true
        };

        var rentRecurring = new TransactionEntry
        {
            UserId = userId,
            Title = "Home Rent",
            Amount = 18000m,
            Kind = TransactionKind.Expense,
            ScheduleType = ScheduleType.Recurring,
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = threeMonthsAgo,
            CategoryId = emiCategoryId,
            PaidFromAccountId = salaryAccount.Id,
            IsActive = true
        };

        var chitRecurring = new TransactionEntry
        {
            UserId = userId,
            Title = "Quarter Chit Contribution",
            Amount = 15000m,
            Kind = TransactionKind.Expense,
            ScheduleType = ScheduleType.Recurring,
            Frequency = RecurrenceFrequency.Every4Months,
            StartDate = new DateOnly(today.Year, 1, 1),
            CategoryId = chitFundCategoryId,
            PaidFromAccountId = salaryAccount.Id,
            IsActive = true
        };

        var internetRecurring = new TransactionEntry
        {
            UserId = userId,
            Title = "Internet + Mobile",
            Amount = 1800m,
            Kind = TransactionKind.Expense,
            ScheduleType = ScheduleType.Recurring,
            Frequency = RecurrenceFrequency.Monthly,
            StartDate = threeMonthsAgo,
            CategoryId = utilitiesCategoryId,
            PaidFromAccountId = salaryAccount.Id,
            IsActive = true
        };

        dbContext.Transactions.AddRange(salaryRecurring, rentRecurring, chitRecurring, internetRecurring);

        dbContext.Transactions.AddRange(
            new TransactionEntry
            {
                UserId = userId,
                Title = "Freelance API Project",
                Amount = 12000m,
                Kind = TransactionKind.Income,
                ScheduleType = ScheduleType.OneTime,
                Date = monthStart.AddDays(5),
                CategoryId = businessCategoryId,
                ReceivedToAccountId = savingsAccount.Id,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            },
            new TransactionEntry
            {
                UserId = userId,
                Title = "Groceries",
                Amount = 4200m,
                Kind = TransactionKind.Expense,
                ScheduleType = ScheduleType.OneTime,
                Date = monthStart.AddDays(3),
                CategoryId = foodCategoryId,
                PaidFromAccountId = salaryAccount.Id,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            },
            new TransactionEntry
            {
                UserId = userId,
                Title = "Weekend Shopping",
                Amount = 6700m,
                Kind = TransactionKind.Expense,
                ScheduleType = ScheduleType.OneTime,
                Date = monthStart.AddDays(14),
                CategoryId = shoppingCategoryId,
                PaidFromAccountId = salaryAccount.Id,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            });

        dbContext.Transactions.AddRange(
            new TransactionEntry
            {
                UserId = userId,
                Title = salaryRecurring.Title,
                Amount = salaryRecurring.Amount,
                Kind = salaryRecurring.Kind,
                ScheduleType = ScheduleType.OneTime,
                Date = monthStart,
                CategoryId = salaryRecurring.CategoryId,
                ReceivedToAccountId = salaryRecurring.ReceivedToAccountId,
                ParentTransactionId = salaryRecurring.Id,
                EntryRole = TransactionEntryRole.RecurringCompletion,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            },
            new TransactionEntry
            {
                UserId = userId,
                Title = rentRecurring.Title,
                Amount = rentRecurring.Amount,
                Kind = rentRecurring.Kind,
                ScheduleType = ScheduleType.OneTime,
                Date = monthStart,
                CategoryId = rentRecurring.CategoryId,
                PaidFromAccountId = rentRecurring.PaidFromAccountId,
                ParentTransactionId = rentRecurring.Id,
                EntryRole = TransactionEntryRole.RecurringCompletion,
                IsCompleted = true,
                CompletedAt = DateTime.Now
            });

        await dbContext.SaveChangesAsync();
    }

    private static async Task<Guid> GetCategoryIdAsync(ApplicationDbContext dbContext, CategoryType type, string name)
    {
        var category = await dbContext.Categories.SingleAsync(c => c.Type == type && c.Name == name);
        return category.Id;
    }
}
