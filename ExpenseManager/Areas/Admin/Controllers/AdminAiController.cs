using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseManager.Data;
using ExpenseManager.Services;
using ExpenseManager.Models;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public class AdminAiController : Controller
{
    private readonly IAdminSettingsService _adminSettings;
    private readonly IAiTokenUsageService _tokenUsage;
    private readonly IConfiguration _config;

    public AdminAiController(IAdminSettingsService adminSettings, IAiTokenUsageService tokenUsage, IConfiguration config)
    {
        _adminSettings = adminSettings;
        _tokenUsage = tokenUsage;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var apiKeyFromDb = await _adminSettings.GetAsync("Gemini:ApiKey", cancellationToken);
        var modelFromDb = await _adminSettings.GetAsync("Gemini:Model", cancellationToken);
        var configKey = _config["Gemini:ApiKey"] ?? "";
        var configModel = _config["Gemini:Model"] ?? "gemini-2.0-flash";
        var model = !string.IsNullOrWhiteSpace(modelFromDb) ? modelFromDb : configModel;
        var (totalPrompt, totalCompletion, totalCalls) = await _tokenUsage.GetTotalsAsync(null, null, null, cancellationToken);
        var history = await _tokenUsage.GetHistoryAsync(50, 0, null, cancellationToken);
        var vm = new AdminAiViewModel
        {
            ApiKeyOverride = apiKeyFromDb ?? "",
            ApiKeyFromConfig = string.IsNullOrEmpty(configKey) ? "" : "***configured***",
            Model = model,
            AvailableModels = new[] { "gemini-2.0-flash", "gemini-1.5-flash", "gemini-1.5-pro", "gemini-1.0-pro" },
            TotalPromptTokens = totalPrompt,
            TotalCompletionTokens = totalCompletion,
            TotalCalls = totalCalls,
            TokenHistory = history
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings([FromForm] string? apiKeyOverride, [FromForm] string? model, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
            await _adminSettings.SetAsync("Gemini:ApiKey", apiKeyOverride.Trim(), cancellationToken);
        if (!string.IsNullOrWhiteSpace(model))
            await _adminSettings.SetAsync("Gemini:Model", model.Trim(), cancellationToken);
        TempData["UserMessage"] = "AI settings saved.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> TokenHistory(int skip = 0, int take = 100, string? userId = null, CancellationToken cancellationToken = default)
    {
        var history = await _tokenUsage.GetHistoryAsync(take, skip, userId, cancellationToken);
        var (totalPrompt, totalCompletion, totalCalls) = await _tokenUsage.GetTotalsAsync(null, null, userId, cancellationToken);
        return View(new AdminTokenHistoryViewModel
        {
            Items = history,
            TotalPromptTokens = totalPrompt,
            TotalCompletionTokens = totalCompletion,
            TotalCalls = totalCalls,
            Skip = skip,
            Take = take,
            UserId = userId
        });
    }
}

public class AdminAiViewModel
{
    public string ApiKeyOverride { get; set; } = "";
    public string ApiKeyFromConfig { get; set; } = "";
    public string Model { get; set; } = "";
    public string[] AvailableModels { get; set; } = Array.Empty<string>();
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public int TotalCalls { get; set; }
    public IReadOnlyList<AiTokenUsage> TokenHistory { get; set; } = new List<AiTokenUsage>();
}

public class AdminTokenHistoryViewModel
{
    public IReadOnlyList<AiTokenUsage> Items { get; set; } = new List<AiTokenUsage>();
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public int TotalCalls { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public string? UserId { get; set; }
}
