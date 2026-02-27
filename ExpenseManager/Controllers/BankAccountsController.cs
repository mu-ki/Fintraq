using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Controllers;

[Authorize]
public class BankAccountsController(
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

        var accounts = await dbContext.BankAccounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.AccountName)
            .ToListAsync();

        ViewBag.Balances = new Dictionary<Guid, decimal>();
        foreach (var account in accounts)
        {
            ViewBag.Balances[account.Id] = await dashboardService.GetCurrentBalanceAsync(userId, account.Id);
        }

        return View(accounts);
    }

    public IActionResult Create()
    {
        return View(new BankAccount());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BankAccount account)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        ValidateBusinessRules(account);

        if (!ModelState.IsValid)
        {
            return View(account);
        }

        account.UserId = userId;
        dbContext.BankAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var account = await dbContext.BankAccounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (account is null)
        {
            return NotFound();
        }

        return View(account);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, BankAccount input)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var account = await dbContext.BankAccounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (account is null)
        {
            return NotFound();
        }

        ValidateBusinessRules(input);

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        account.AccountName = input.AccountName;
        account.AccountType = input.AccountType;
        account.InitialBalance = input.InitialBalance;
        account.UseManualOverride = input.UseManualOverride;
        account.ManualBalanceOverride = input.ManualBalanceOverride;
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
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

        var account = await dbContext.BankAccounts.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (account is null)
        {
            return NotFound();
        }

        account.IsDeleted = true;
        account.UpdatedAt = DateTime.Now;
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private void ValidateBusinessRules(BankAccount account)
    {
        if (account.UseManualOverride && !account.ManualBalanceOverride.HasValue)
        {
            ModelState.AddModelError(nameof(account.ManualBalanceOverride), "Manual Balance Override is required when manual override is enabled.");
        }
    }
}
