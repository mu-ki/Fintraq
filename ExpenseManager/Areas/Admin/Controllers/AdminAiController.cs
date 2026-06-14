using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseManager.Data;
using ExpenseManager.Models;
using ExpenseManager.Models.Ai;
using ExpenseManager.Services;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public class AdminAiController(
    IAdminSettingsService adminSettings,
    IAiTokenUsageService tokenUsage,
    IAiModelsService aiModels,
    IAiOptionsProvider aiOptions,
    IConfiguration config) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var provider = await aiOptions.GetProviderAsync(cancellationToken);
        var providerSettings = new Dictionary<AiProvider, ProviderAiSettings>();
        foreach (var p in Enum.GetValues<AiProvider>())
        {
            providerSettings[p] = await BuildProviderSettingsAsync(p, cancellationToken);
        }

        var active = providerSettings[provider];
        var (totalPrompt, totalCompletion, totalCalls) = await tokenUsage.GetTotalsAsync(null, null, null, cancellationToken);
        var history = await tokenUsage.GetHistoryAsync(50, 0, null, cancellationToken);
        var vm = new AdminAiViewModel
        {
            Provider = provider,
            ApiKeyOverride = active.ApiKeyOverride,
            ApiKeyFromConfig = active.ApiKeyFromConfig,
            Model = active.Model,
            AvailableModels = active.AvailableModels,
            ProviderSettings = providerSettings,
            TotalPromptTokens = totalPrompt,
            TotalCompletionTokens = totalCompletion,
            TotalCalls = totalCalls,
            TokenHistory = history
        };
        return View(vm);
    }

    private async Task<ProviderAiSettings> BuildProviderSettingsAsync(AiProvider provider, CancellationToken cancellationToken)
    {
        var keyDb = provider switch
        {
            AiProvider.Anthropic => await adminSettings.GetAsync("Anthropic:ApiKey", cancellationToken),
            AiProvider.Cursor => await adminSettings.GetAsync("Cursor:ApiKey", cancellationToken),
            _ => await adminSettings.GetAsync("Gemini:ApiKey", cancellationToken)
        };
        var modelDb = await GetModelFromDbAsync(provider, cancellationToken);
        var configKey = provider switch
        {
            AiProvider.Anthropic => config["Anthropic:ApiKey"] ?? "",
            AiProvider.Cursor => config["Cursor:ApiKey"] ?? "",
            _ => config["Gemini:ApiKey"] ?? ""
        };
        var configModel = provider switch
        {
            AiProvider.Anthropic => config["Anthropic:Model"] ?? "claude-sonnet-4-20250514",
            AiProvider.Cursor => config["Cursor:Model"] ?? "composer-2",
            _ => config["Gemini:Model"] ?? "gemini-2.0-flash"
        };
        var effectiveKey = !string.IsNullOrWhiteSpace(keyDb) ? keyDb : configKey;
        var model = !string.IsNullOrWhiteSpace(modelDb) ? modelDb : configModel;
        var availableModels = (await aiModels.ListModelsAsync(provider, effectiveKey, cancellationToken)).ToList();
        if (availableModels.Count == 0)
        {
            availableModels = provider switch
            {
                AiProvider.Anthropic => ["claude-sonnet-4-20250514", "claude-3-5-sonnet-20241022"],
                AiProvider.Cursor => ["composer-2"],
                _ => ["gemini-2.0-flash", "gemini-1.5-flash"]
            };
        }

        if (!string.IsNullOrEmpty(model) && !availableModels.Contains(model))
        {
            availableModels.Insert(0, model);
        }

        return new ProviderAiSettings
        {
            ApiKeyOverride = keyDb ?? "",
            ApiKeyFromConfig = string.IsNullOrEmpty(configKey) ? "" : "***configured***",
            Model = model,
            AvailableModels = availableModels.ToArray(),
            HelpText = provider switch
            {
                AiProvider.Anthropic => "Anthropic API key from console.anthropic.com.",
                AiProvider.Cursor => "Cursor Cloud Agents API key from Cursor Dashboard → API Keys. Replies may take longer than Gemini/Anthropic.",
                _ => "Google AI Studio / Gemini API key."
            }
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [FromForm] string? provider,
        [FromForm] string? apiKeyOverride,
        [FromForm] string? model,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(provider) && Enum.TryParse<AiProvider>(provider, true, out var parsedProvider))
        {
            await adminSettings.SetAsync("Ai:Provider", parsedProvider.ToString(), cancellationToken);
        }

        var activeProvider = !string.IsNullOrWhiteSpace(provider) && Enum.TryParse<AiProvider>(provider, true, out var p)
            ? p
            : await aiOptions.GetProviderAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
        {
            var apiKeySetting = activeProvider switch
            {
                AiProvider.Anthropic => "Anthropic:ApiKey",
                AiProvider.Cursor => "Cursor:ApiKey",
                _ => "Gemini:ApiKey"
            };
            await adminSettings.SetAsync(apiKeySetting, apiKeyOverride.Trim(), cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            var modelKey = activeProvider switch
            {
                AiProvider.Anthropic => "Anthropic:Model",
                AiProvider.Cursor => "Cursor:Model",
                _ => "Gemini:Model"
            };
            await adminSettings.SetAsync(modelKey, model.Trim(), cancellationToken);
        }

        TempData["UserMessage"] = "AI settings saved.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> TokenHistory(int skip = 0, int take = 100, string? userId = null, CancellationToken cancellationToken = default)
    {
        var history = await tokenUsage.GetHistoryAsync(take, skip, userId, cancellationToken);
        var (totalPrompt, totalCompletion, totalCalls) = await tokenUsage.GetTotalsAsync(null, null, userId, cancellationToken);
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

    private async Task<string?> GetModelFromDbAsync(AiProvider provider, CancellationToken cancellationToken)
    {
        return provider switch
        {
            AiProvider.Anthropic => await adminSettings.GetAsync("Anthropic:Model", cancellationToken),
            AiProvider.Cursor => await adminSettings.GetAsync("Cursor:Model", cancellationToken),
            _ => await adminSettings.GetAsync("Gemini:Model", cancellationToken)
        };
    }
}

public class AdminAiViewModel
{
    public AiProvider Provider { get; set; } = AiProvider.Gemini;
    public string ApiKeyOverride { get; set; } = "";
    public string ApiKeyFromConfig { get; set; } = "";
    public string Model { get; set; } = "";
    public string[] AvailableModels { get; set; } = Array.Empty<string>();
    public Dictionary<AiProvider, ProviderAiSettings> ProviderSettings { get; set; } = new();
    public int TotalPromptTokens { get; set; }
    public int TotalCompletionTokens { get; set; }
    public int TotalCalls { get; set; }
    public IReadOnlyList<AiTokenUsage> TokenHistory { get; set; } = new List<AiTokenUsage>();
}

public class ProviderAiSettings
{
    public string ApiKeyOverride { get; set; } = "";
    public string ApiKeyFromConfig { get; set; } = "";
    public string Model { get; set; } = "";
    public string[] AvailableModels { get; set; } = Array.Empty<string>();
    public string HelpText { get; set; } = "";
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
