using ExpenseManager.Data;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public sealed class AdminTelegramController(
    IAdminSettingsService adminSettings,
    ITelegramOptionsProvider telegramOptions,
    ITelegramAdminService telegramAdmin,
    IConfiguration config) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await telegramOptions.GetSettingsAsync(cancellationToken);
        var configToken = config["Telegram:BotToken"] ?? "";
        var configSecret = config["Telegram:WebhookSecret"] ?? "";
        var configUsername = config["Telegram:BotUsername"] ?? "";
        var configWebhookUrl = config["Telegram:WebhookUrl"] ?? "";

        var defaultWebhookUrl = $"{Request.Scheme}://{Request.Host}/api/webhooks/telegram";
        var (statusOk, statusMessage) = settings.IsConfigured
            ? await telegramAdmin.GetWebhookStatusAsync(cancellationToken)
            : (false, "Configure bot token to check webhook status.");

        var vm = new AdminTelegramViewModel
        {
            BotTokenOverride = await adminSettings.GetAsync("Telegram:BotToken", cancellationToken) ?? "",
            WebhookSecretOverride = await adminSettings.GetAsync("Telegram:WebhookSecret", cancellationToken) ?? "",
            BotUsernameOverride = await adminSettings.GetAsync("Telegram:BotUsername", cancellationToken) ?? "",
            WebhookUrlOverride = await adminSettings.GetAsync("Telegram:WebhookUrl", cancellationToken) ?? "",
            BotTokenFromConfig = string.IsNullOrEmpty(configToken) ? "" : "***configured***",
            WebhookSecretFromConfig = string.IsNullOrEmpty(configSecret) ? "" : "***configured***",
            BotUsernameFromConfig = configUsername,
            WebhookUrlFromConfig = configWebhookUrl,
            EffectiveWebhookUrl = !string.IsNullOrWhiteSpace(settings.WebhookUrl) ? settings.WebhookUrl : defaultWebhookUrl,
            IsConfigured = settings.IsConfigured,
            WebhookStatus = statusOk ? statusMessage : statusMessage
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [FromForm] string? botTokenOverride,
        [FromForm] string? webhookSecretOverride,
        [FromForm] string? botUsernameOverride,
        [FromForm] string? webhookUrlOverride,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(botTokenOverride))
        {
            await adminSettings.SetAsync("Telegram:BotToken", botTokenOverride.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(webhookSecretOverride))
        {
            await adminSettings.SetAsync("Telegram:WebhookSecret", webhookSecretOverride.Trim(), cancellationToken);
        }

        if (botUsernameOverride is not null)
        {
            await adminSettings.SetAsync("Telegram:BotUsername", botUsernameOverride.Trim(), cancellationToken);
        }

        if (webhookUrlOverride is not null)
        {
            await adminSettings.SetAsync("Telegram:WebhookUrl", webhookUrlOverride.Trim(), cancellationToken);
        }

        TempData["UserMessage"] = "Telegram settings saved.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterWebhook(CancellationToken cancellationToken)
    {
        var settings = await telegramOptions.GetSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            var defaultUrl = $"{Request.Scheme}://{Request.Host}/api/webhooks/telegram";
            await adminSettings.SetAsync("Telegram:WebhookUrl", defaultUrl, cancellationToken);
        }

        var (success, message) = await telegramAdmin.RegisterWebhookAsync(cancellationToken);
        TempData["UserMessage"] = message;
        TempData["UserMessageType"] = success ? "success" : "danger";
        return RedirectToAction(nameof(Index));
    }
}

public sealed class AdminTelegramViewModel
{
    public string BotTokenOverride { get; set; } = "";
    public string WebhookSecretOverride { get; set; } = "";
    public string BotUsernameOverride { get; set; } = "";
    public string WebhookUrlOverride { get; set; } = "";
    public string BotTokenFromConfig { get; set; } = "";
    public string WebhookSecretFromConfig { get; set; } = "";
    public string BotUsernameFromConfig { get; set; } = "";
    public string WebhookUrlFromConfig { get; set; } = "";
    public string EffectiveWebhookUrl { get; set; } = "";
    public bool IsConfigured { get; set; }
    public string WebhookStatus { get; set; } = "";
}
