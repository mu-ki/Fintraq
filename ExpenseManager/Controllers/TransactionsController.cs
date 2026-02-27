using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.ViewModels;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Controllers;

[Authorize]
public class TransactionsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IDashboardService dashboardService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transactions = await dbContext.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId && t.EntryRole == TransactionEntryRole.Standard)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);

        bool IsCompletedOrInactive(TransactionEntry t)
        {
            if (t.ScheduleType == ScheduleType.OneTime)
            {
                return t.Date.HasValue && t.Date.Value < today;
            }

            return !t.IsActive || (t.EndDate.HasValue && t.EndDate.Value < today);
        }

        var vm = new TransactionListViewModel
        {
            ActiveTransactions = transactions
                .Where(t => !IsCompletedOrInactive(t))
                .OrderByDescending(t => t.CreatedAt)
                .ToList(),
            CompletedTransactions = transactions
                .Where(IsCompletedOrInactive)
                .OrderByDescending(t => t.UpdatedAt)
                .ToList()
        };

        return View(vm);
    }

    public async Task<IActionResult> Create()
    {
        var vm = new TransactionCreateViewModel
        {
            Categories = await dbContext.Categories.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(),
            Accounts = await AccountsForCurrentUser()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TransactionCreateViewModel vm)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        ValidateBusinessRules(vm);

        if (!ModelState.IsValid)
        {
            vm.Categories = await dbContext.Categories.OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync();
            vm.Accounts = await AccountsForCurrentUser();
            return View(vm);
        }

        var entity = new TransactionEntry
        {
            UserId = userId,
            Title = vm.Title.Trim(),
            Amount = vm.Amount,
            CategoryId = vm.CategoryId!.Value,
            Kind = vm.Kind,
            ScheduleType = vm.ScheduleType,
            Frequency = vm.ScheduleType == ScheduleType.Recurring ? vm.Frequency : null,
            Date = vm.ScheduleType == ScheduleType.OneTime ? vm.Date : null,
            StartDate = vm.ScheduleType == ScheduleType.Recurring ? vm.StartDate : null,
            EndDate = vm.ScheduleType == ScheduleType.Recurring ? vm.EndDate : null,
            PaidFromAccountId = vm.PaidFromAccountId,
            ReceivedToAccountId = vm.ReceivedToAccountId,
            RecurrenceGroupId = vm.ScheduleType == ScheduleType.Recurring ? Guid.NewGuid() : null,
            IsCompleted = vm.ScheduleType == ScheduleType.OneTime
        };

        dbContext.Transactions.Add(entity);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> UpdateFuture(Guid id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var recurring = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == id &&
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (recurring is null)
        {
            return NotFound();
        }

        var vm = new RecurringUpdateFutureViewModel
        {
            TransactionId = recurring.Id,
            NewAmount = recurring.Amount,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.Now)
        };
        return View(vm);
    }

    public async Task<IActionResult> EditDates(Guid id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == id &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (transaction is null)
        {
            return NotFound();
        }

        var vm = new TransactionEditDatesViewModel
        {
            TransactionId = transaction.Id,
            Title = transaction.Title,
            ScheduleType = transaction.ScheduleType,
            Frequency = transaction.Frequency,
            Date = transaction.Date,
            StartDate = transaction.StartDate,
            EndDate = transaction.EndDate
        };

        return View(vm);
    }

    public async Task<IActionResult> EditAmount(Guid id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == id &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (transaction is null)
        {
            return NotFound();
        }

        var vm = new TransactionEditAmountViewModel
        {
            TransactionId = transaction.Id,
            Title = transaction.Title,
            ScheduleType = transaction.ScheduleType,
            Amount = transaction.Amount
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAmount(TransactionEditAmountViewModel vm)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == vm.TransactionId &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (transaction is null)
        {
            return NotFound();
        }

        if (vm.Amount <= 0)
        {
            ModelState.AddModelError(nameof(vm.Amount), "Amount must be greater than zero.");
        }

        if (!ModelState.IsValid)
        {
            vm.Title = transaction.Title;
            vm.ScheduleType = transaction.ScheduleType;
            return View(vm);
        }

        transaction.Amount = vm.Amount;
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDates(TransactionEditDatesViewModel vm)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == vm.TransactionId &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (transaction is null)
        {
            return NotFound();
        }

        if (transaction.ScheduleType == ScheduleType.OneTime)
        {
            if (!vm.Date.HasValue)
            {
                ModelState.AddModelError(nameof(vm.Date), "Date is required for one-time transactions.");
            }
        }
        else
        {
            if (!vm.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "Start Date is required for recurring transactions.");
            }

            if (vm.StartDate.HasValue && vm.EndDate.HasValue && vm.EndDate.Value < vm.StartDate.Value)
            {
                ModelState.AddModelError(nameof(vm.EndDate), "End Date must be on or after Start Date.");
            }
        }

        if (!ModelState.IsValid)
        {
            vm.Title = transaction.Title;
            vm.ScheduleType = transaction.ScheduleType;
            vm.Frequency = transaction.Frequency;
            return View(vm);
        }

        if (transaction.ScheduleType == ScheduleType.OneTime)
        {
            transaction.Date = vm.Date;
        }
        else
        {
            transaction.StartDate = vm.StartDate;
            transaction.EndDate = vm.EndDate;
        }

        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateFuture(RecurringUpdateFutureViewModel vm)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var current = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == vm.TransactionId &&
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (current is null)
        {
            return NotFound();
        }

        if (!current.StartDate.HasValue || vm.EffectiveFrom <= current.StartDate.Value)
        {
            ModelState.AddModelError(nameof(vm.EffectiveFrom), "Effective date must be after the current recurring start date.");
            return View(vm);
        }

        var originalEnd = current.EndDate;
        current.EndDate = vm.EffectiveFrom.AddDays(-1);

        var nextVersion = new TransactionEntry
        {
            UserId = current.UserId,
            Title = current.Title,
            Amount = vm.NewAmount,
            CategoryId = current.CategoryId,
            Kind = current.Kind,
            ScheduleType = ScheduleType.Recurring,
            Frequency = current.Frequency,
            StartDate = vm.EffectiveFrom,
            EndDate = originalEnd,
            IsActive = current.IsActive,
            PaidFromAccountId = current.PaidFromAccountId,
            ReceivedToAccountId = current.ReceivedToAccountId,
            RecurrenceGroupId = current.RecurrenceGroupId ?? Guid.NewGuid()
        };

        dbContext.Transactions.Add(nextVersion);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> MarkCompleted(Guid id, int year, int month)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var recurring = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == id &&
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (recurring is null)
        {
            return NotFound();
        }

        if (!dashboardService.IsDueInMonth(recurring, year, month))
        {
            TempData["Error"] = "This recurring item is not due in the selected month.";
            return RedirectToAction("Index", "Dashboard", new { year, month });
        }

        var monthDate = new DateOnly(year, month, 1);
        var existing = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.ParentTransactionId == recurring.Id &&
                t.Date == monthDate)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        var vm = new MarkCompletionViewModel
        {
            TransactionId = recurring.Id,
            Year = year,
            Month = month,
            Title = recurring.Title,
            Kind = recurring.Kind,
            Amount = existing?.Amount ?? recurring.Amount
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkCompleted(MarkCompletionViewModel vm)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var recurring = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == vm.TransactionId &&
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (recurring is null)
        {
            return NotFound();
        }

        if (!dashboardService.IsDueInMonth(recurring, vm.Year, vm.Month))
        {
            TempData["Error"] = "This recurring item is not due in the selected month.";
            return RedirectToAction("Index", "Dashboard", new { year = vm.Year, month = vm.Month });
        }

        if (vm.Amount <= 0)
        {
            ModelState.AddModelError(nameof(vm.Amount), "Amount must be greater than zero.");
        }

        if (!ModelState.IsValid)
        {
            vm.Title = recurring.Title;
            vm.Kind = recurring.Kind;
            return View(vm);
        }

        var monthDate = new DateOnly(vm.Year, vm.Month, 1);
        var existing = await dbContext.Transactions
            .Where(t =>
            t.UserId == userId &&
            t.EntryRole == TransactionEntryRole.RecurringCompletion &&
            t.ParentTransactionId == recurring.Id &&
            t.Date == monthDate)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is null)
        {
            dbContext.Transactions.Add(new TransactionEntry
            {
                UserId = userId,
                Title = recurring.Title,
                Amount = vm.Amount,
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
            existing.Amount = vm.Amount;
            existing.CompletedAt = DateTime.Now;
            existing.IsCompleted = true;
        }

        await dbContext.SaveChangesAsync();

        return RedirectToAction("Index", "Dashboard", new { year = vm.Year, month = vm.Month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllCompleted(int year, int month)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var recurringItems = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.ScheduleType == ScheduleType.Recurring &&
                t.EntryRole == TransactionEntryRole.Standard &&
                t.IsActive)
            .ToListAsync();

        var monthDate = new DateOnly(year, month, 1);

        var existingParentIds = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.Date == monthDate &&
                t.ParentTransactionId.HasValue)
            .Select(t => t.ParentTransactionId!.Value)
            .ToListAsync();

        var pendingDueItems = recurringItems
            .Where(t => dashboardService.IsDueInMonth(t, year, month) && !existingParentIds.Contains(t.Id))
            .ToList();

        foreach (var recurring in pendingDueItems)
        {
            dbContext.Transactions.Add(new TransactionEntry
            {
                UserId = userId,
                Title = recurring.Title,
                Amount = recurring.Amount,
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

        await dbContext.SaveChangesAsync();
        return RedirectToAction("Index", "Dashboard", new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevertCompleted(Guid id, int year, int month)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var monthDate = new DateOnly(year, month, 1);
        var completion = await dbContext.Transactions
            .Where(t =>
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.RecurringCompletion &&
                t.ParentTransactionId == id &&
                t.Date == monthDate)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (completion is not null)
        {
            completion.IsDeleted = true;
            completion.UpdatedAt = DateTime.Now;
            await dbContext.SaveChangesAsync();
        }

        return RedirectToAction("Index", "Dashboard", new { year, month });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var transaction = await dbContext.Transactions
            .SingleOrDefaultAsync(t =>
                t.Id == id &&
                t.UserId == userId &&
                t.EntryRole == TransactionEntryRole.Standard);
        if (transaction is null)
        {
            return NotFound();
        }

        transaction.IsDeleted = true;
        transaction.UpdatedAt = DateTime.Now;
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    private void ValidateBusinessRules(TransactionCreateViewModel vm)
    {
        if (!vm.CategoryId.HasValue || vm.CategoryId.Value == Guid.Empty)
        {
            ModelState.AddModelError(nameof(vm.CategoryId), "Category is required.");
        }

        if (vm.Kind == TransactionKind.Expense && !vm.PaidFromAccountId.HasValue)
        {
            ModelState.AddModelError(nameof(vm.PaidFromAccountId), "PaidFromAccount is mandatory for all expenses.");
        }

        if (vm.ScheduleType == ScheduleType.OneTime && !vm.Date.HasValue)
        {
            ModelState.AddModelError(nameof(vm.Date), "Date is required for one-time transactions.");
        }

        if (vm.ScheduleType == ScheduleType.Recurring)
        {
            if (!vm.StartDate.HasValue)
            {
                ModelState.AddModelError(nameof(vm.StartDate), "StartDate is required for recurring transactions.");
            }

            if (!vm.Frequency.HasValue)
            {
                ModelState.AddModelError(nameof(vm.Frequency), "Frequency is required for recurring transactions.");
            }

            if (vm.StartDate.HasValue && vm.EndDate.HasValue && vm.EndDate.Value < vm.StartDate.Value)
            {
                ModelState.AddModelError(nameof(vm.EndDate), "EndDate must be on or after StartDate.");
            }
        }
    }

    private async Task<List<BankAccount>> AccountsForCurrentUser()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<BankAccount>();
        }

        return await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync();
    }
}
