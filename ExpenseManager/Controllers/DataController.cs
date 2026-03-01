using System.Text.Json;
using ExpenseManager.Models.Export;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Controllers;

[Authorize]
public class DataController(
    IExportImportService exportImportService,
    UserManager<IdentityUser> userManager) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var data = await exportImportService.ExportAsync(userId, cancellationToken);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var fileName = $"fintraq-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (file == null || file.Length == 0)
        {
            TempData["ImportError"] = "Please select a file to import.";
            return RedirectToAction(nameof(Index));
        }

        if (file.Length > 10 * 1024 * 1024) // 10 MB
        {
            TempData["ImportError"] = "File is too large. Maximum size is 10 MB.";
            return RedirectToAction(nameof(Index));
        }

        await using var stream = file.OpenReadStream();
        var result = await exportImportService.ImportAsync(userId, stream, cancellationToken);

        if (result.Success)
        {
            TempData["ImportSuccess"] = $"Import completed: {result.AccountsImported} bank account(s), {result.TransactionsImported} transaction(s).";
            if (result.Errors.Count > 0)
            {
                TempData["ImportWarnings"] = string.Join(" ", result.Errors);
            }
        }
        else
        {
            TempData["ImportError"] = result.Errors.Count > 0
                ? string.Join(" ", result.Errors)
                : "Import failed.";
        }

        return RedirectToAction(nameof(Index));
    }
}
