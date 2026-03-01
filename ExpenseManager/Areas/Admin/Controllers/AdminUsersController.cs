using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ExpenseManager.Data;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public class AdminUsersController : Controller
{
    private readonly UserManager<IdentityUser> _userManager;

    public AdminUsersController(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var users = _userManager.Users
            .OrderBy(u => u.Email)
            .ToList();
        var model = new List<AdminUserViewModel>();
        foreach (var u in users)
        {
            var isLocked = u.LockoutEnabled && u.LockoutEnd.HasValue && u.LockoutEnd.Value > DateTimeOffset.UtcNow;
            model.Add(new AdminUserViewModel
            {
                Id = u.Id,
                Email = u.Email ?? u.UserName ?? "",
                UserName = u.UserName ?? "",
                EmailConfirmed = u.EmailConfirmed,
                IsActive = !isLocked,
                LockoutEnd = u.LockoutEnd
            });
        }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var isLocked = user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;
        return View(new AdminUserViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? "",
            UserName = user.UserName ?? "",
            EmailConfirmed = user.EmailConfirmed,
            IsActive = !isLocked
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(string id, [FromForm] string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["UserMessage"] = "Password must be at least 6 characters.";
            TempData["UserMessageType"] = "danger";
            return RedirectToAction(nameof(Edit), new { id });
        }
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            TempData["UserMessage"] = "Password updated successfully.";
            TempData["UserMessageType"] = "success";
        }
        else
        {
            TempData["UserMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
            TempData["UserMessageType"] = "danger";
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string id, [FromForm] bool active, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        if (active)
        {
            await _userManager.SetLockoutEnabledAsync(user, false);
            await _userManager.SetLockoutEndDateAsync(user, null);
        }
        else
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(10));
        }
        TempData["UserMessage"] = active ? "User activated." : "User deactivated.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Edit), new { id });
    }
}

public class AdminUserViewModel
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string UserName { get; set; } = "";
    public bool EmailConfirmed { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
}
