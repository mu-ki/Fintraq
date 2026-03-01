using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseManager.Data;
using ExpenseManager.Services;

namespace ExpenseManager.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = SeedData.AdminRoleName)]
public class AdminLogsController : Controller
{
    private readonly IAdminSettingsService _adminSettings;
    private readonly ILogReaderService _logReader;
    private readonly IConfiguration _config;
    private readonly IHostApplicationLifetime _lifetime;

    public AdminLogsController(IAdminSettingsService adminSettings, ILogReaderService logReader, IConfiguration config, IHostApplicationLifetime lifetime)
    {
        _adminSettings = adminSettings;
        _logReader = logReader;
        _config = config;
        _lifetime = lifetime;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int tail = 200, CancellationToken cancellationToken = default)
    {
        var currentLevel = await _adminSettings.GetAsync("Logging:LogLevel:Default", cancellationToken)
            ?? _config["Logging:LogLevel:Default"] ?? "Information";
        var entries = await _logReader.GetRecentAsync(tail, cancellationToken);
        var vm = new AdminLogsViewModel
        {
            LogEntries = entries,
            CurrentLogLevel = currentLevel,
            LogLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" },
            Tail = tail
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetLevel([FromForm] string level, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(level))
        {
            await _adminSettings.SetAsync("Logging:LogLevel:Default", level.Trim(), cancellationToken);
            TempData["UserMessage"] = $"Log level set to {level}. Restart the application for it to take effect everywhere.";
            TempData["UserMessageType"] = "success";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WipeLogs(CancellationToken cancellationToken = default)
    {
        await _logReader.WipeAsync(cancellationToken);
        TempData["UserMessage"] = "Log buffer cleared.";
        TempData["UserMessageType"] = "success";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Stops the application gracefully. If run under a process manager (e.g. systemd, Docker, or a script that restarts on exit), it will restart automatically.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RestartApplication()
    {
        Response.OnCompleted(() =>
        {
            _lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return View("Restarting");
    }
}

public class AdminLogsViewModel
{
    public IReadOnlyList<ExpenseManager.Services.LogEntry> LogEntries { get; set; } = new List<ExpenseManager.Services.LogEntry>();
    public string CurrentLogLevel { get; set; } = "Information";
    public string[] LogLevels { get; set; } = Array.Empty<string>();
    public int Tail { get; set; }
}

