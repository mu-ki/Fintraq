using ExpenseManager.Services;
using ExpenseManager.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Controllers;

[Authorize]
[Route("Settings/Messaging")]
public sealed class MessagingSettingsController(
    IMessagingLinkService linkService,
    UserManager<IdentityUser> userManager,
    ITelegramOptionsProvider telegramOptions) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var links = await linkService.GetLinksForUserAsync(userId, cancellationToken);
        var telegram = await telegramOptions.GetSettingsAsync(cancellationToken);
        ViewBag.TelegramBotUsername = telegram.BotUsername;
        ViewBag.TelegramConfigured = telegram.IsConfigured;
        return View(links);
    }

    [HttpPost("GenerateCode")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateCode(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var (plainCode, expiresAt) = await linkService.GenerateLinkCodeAsync(userId, cancellationToken);
        TempData["LinkCode"] = plainCode;
        TempData["LinkCodeExpiresAtUtc"] = expiresAt.ToUtcIsoString();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Revoke/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        await linkService.RevokeLinkAsync(userId, id, cancellationToken);
        TempData["Revoked"] = true;
        return RedirectToAction(nameof(Index));
    }
}
