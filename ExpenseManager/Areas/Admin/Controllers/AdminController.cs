using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Data.SeedData.AdminRoleName)]
public class AdminController : Controller
{
    public IActionResult Index() => View();
}
