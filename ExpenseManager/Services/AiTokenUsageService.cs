using ExpenseManager.Data;
using ExpenseManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class AiTokenUsageService(ApplicationDbContext db) : IAiTokenUsageService
{
    public async Task RecordAsync(string userId, string model, int promptTokens, int completionTokens, CancellationToken cancellationToken = default)
    {
        db.AiTokenUsages.Add(new AiTokenUsage
        {
            UserId = userId,
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            CalledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiTokenUsage>> GetHistoryAsync(int take = 100, int skip = 0, string? userId = null, CancellationToken cancellationToken = default)
    {
        var query = db.AiTokenUsages.AsNoTracking().OrderByDescending(u => u.CalledAt);
        if (!string.IsNullOrEmpty(userId))
            query = (IOrderedQueryable<AiTokenUsage>)query.Where(u => u.UserId == userId);
        return await query.Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<(int TotalPrompt, int TotalCompletion, int TotalCalls)> GetTotalsAsync(DateTime? from = null, DateTime? to = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        var query = db.AiTokenUsages.AsNoTracking();
        if (from.HasValue) query = query.Where(u => u.CalledAt >= from.Value);
        if (to.HasValue) query = query.Where(u => u.CalledAt <= to.Value);
        if (!string.IsNullOrEmpty(userId)) query = query.Where(u => u.UserId == userId);
        var list = await query.ToListAsync(cancellationToken);
        return (list.Sum(u => u.PromptTokens), list.Sum(u => u.CompletionTokens), list.Count);
    }
}
