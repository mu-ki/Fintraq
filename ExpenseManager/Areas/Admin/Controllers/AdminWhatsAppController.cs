using ExpenseManager.Data;
using ExpenseManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public sealed class AdminWhatsAppController(
    IAdminSettingsService adminSettings,
    IWhatsAppOptionsProvider whatsAppOptions,
    IWhatsAppAdminService whatsAppAdmin,
    IConfiguration config) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var settings = await whatsAppOptions.GetSettingsAsync(cancellationToken);
        var configToken = config["WhatsApp:AccessToken"] ?? "";
        var configPhoneNumberId = config["WhatsApp:PhoneNumberId"] ?? "";
        var configVerifyToken = config["WhatsApp:VerifyToken"] ?? "";
        var configAppSecret = config["WhatsApp:AppSecret"] ?? "";
        var configWebhookUrl = config["WhatsApp:WebhookUrl"] ?? "";

        var defaultWebhookUrl = $"{Request.Scheme}://{Request.Host}/api/webhooks/whatsapp";
        var (statusOk, statusMessage) = settings.IsConfigured
            ? await whatsAppAdmin.TestConnectionAsync(cancellationToken)
            : (false, "Configure access token and phone number ID to test the connection.");

        var vm = new AdminWhatsAppViewModel
        {
            AccessTokenOverride = await adminSettings.GetAsync("WhatsApp:AccessToken", cancellationToken) ?? "",
            PhoneNumberIdOverride = await adminSettings.GetAsync("WhatsApp:PhoneNumberId", cancellationToken) ?? "",
            VerifyTokenOverride = await adminSettings.GetAsync("WhatsApp:VerifyToken", cancellationToken) ?? "",
            AppSecretOverride = await adminSettings.GetAsync("WhatsApp:AppSecret", cancellationToken) ?? "",
            WebhookUrlOverride = await adminSettings.GetAsync("WhatsApp:WebhookUrl", cancellationToken) ?? "",
            AccessTokenFromConfig = string.IsNullOrEmpty(configToken) ? "" : "***configured***",
            PhoneNumberIdFromConfig = configPhoneNumberId,
            VerifyTokenFromConfig = string.IsNullOrEmpty(configVerifyToken) ? "" : "***configured***",
            AppSecretFromConfig = string.IsNullOrEmpty(configAppSecret) ? "" : "***configured***",
            WebhookUrlFromConfig = configWebhookUrl,
            EffectiveWebhookUrl = !string.IsNullOrWhiteSpace(settings.WebhookUrl) ? settings.WebhookUrl : defaultWebhookUrl,
            EffectiveVerifyToken = !string.IsNullOrWhiteSpace(settings.VerifyToken)
                ? settings.VerifyToken
                : (string.IsNullOrEmpty(configVerifyToken) ? "(not set)" : "***configured***"),
            IsConfigured = settings.IsConfigured,
            ConnectionStatus = statusMessage
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [FromForm] string? accessTokenOverride,
        [FromForm] string? phoneNumberIdOverride,
        [FromForm] string? verifyTokenOverride,
        [FromForm] string? appSecretOverride,
        [FromForm] string? webhookUrlOverride,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessTokenOverride))
        {
            await adminSettings.SetAsync("WhatsApp:AccessToken", accessTokenOverride.Trim(), cancellationToken);
        }

        if (phoneNumberIdOverride is not null)
        {
            await adminSettings.SetAsync("WhatsApp:PhoneNumberId", phoneNumberIdOverride.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(verifyTokenOverride))
        {
            await adminSettings.SetAsync("WhatsApp:VerifyToken", verifyTokenOverride.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(appSecretOverride))
        {
            await adminSettings.SetAsync("WhatsApp:AppSecret", appSecretOverride.Trim(), cancellationToken);
        }

        if (webhookUrlOverride is not null)
        {
            await adminSettings.SetAsync("WhatsApp:WebhookUrl", webhookUrlOverride.Trim(), cancellationToken);
        }

        TempData["UserMessage"] = "WhatsApp settings saved.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(CancellationToken cancellationToken)
    {
        var (success, message) = await whatsAppAdmin.TestConnectionAsync(cancellationToken);
        TempData["UserMessage"] = message;
        TempData["UserMessageType"] = success ? "success" : "danger";
        return RedirectToAction(nameof(Index));
    }
}

public sealed class AdminWhatsAppViewModel
{
    public string AccessTokenOverride { get; set; } = "";
    public string PhoneNumberIdOverride { get; set; } = "";
    public string VerifyTokenOverride { get; set; } = "";
    public string AppSecretOverride { get; set; } = "";
    public string WebhookUrlOverride { get; set; } = "";
    public string AccessTokenFromConfig { get; set; } = "";
    public string PhoneNumberIdFromConfig { get; set; } = "";
    public string VerifyTokenFromConfig { get; set; } = "";
    public string AppSecretFromConfig { get; set; } = "";
    public string WebhookUrlFromConfig { get; set; } = "";
    public string EffectiveWebhookUrl { get; set; } = "";
    public string EffectiveVerifyToken { get; set; } = "";
    public bool IsConfigured { get; set; }
    public string ConnectionStatus { get; set; } = "";
}
