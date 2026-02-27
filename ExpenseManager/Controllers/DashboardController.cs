using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Controllers;

[Authorize]
public class DashboardController(IDashboardService dashboardService, UserManager<IdentityUser> userManager) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month)
    {
        var now = DateTime.Now;
        var selectedYear = year ?? now.Year;
        var selectedMonth = month ?? now.Month;
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var model = await dashboardService.BuildMonthAsync(userId, selectedYear, selectedMonth);
        return View(model);
    }
}
