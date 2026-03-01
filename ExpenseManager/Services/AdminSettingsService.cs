using ExpenseManager.Data;
using ExpenseManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class AdminSettingsService(ApplicationDbContext db) : IAdminSettingsService
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await db.AdminSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var setting = await db.AdminSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        var now = DateTime.UtcNow;
        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }
        else
        {
            db.AdminSettings.Add(new AdminSetting { Key = key, Value = value, UpdatedAt = now });
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
