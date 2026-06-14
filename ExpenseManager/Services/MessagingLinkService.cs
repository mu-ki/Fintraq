using System.Security.Cryptography;
using System.Text;
using ExpenseManager.Data;
using ExpenseManager.Models.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class MessagingLinkService(ApplicationDbContext dbContext) : IMessagingLinkService
{
    private const int CodeLength = 6;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    public async Task<(string PlainCode, DateTime ExpiresAt)> GenerateLinkCodeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var plainCode = GenerateNumericCode();
        var expiresAt = DateTime.UtcNow.Add(CodeLifetime);

        var activeCodes = await dbContext.MessagingLinkCodes
            .Where(c => c.UserId == userId && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var existing in activeCodes)
        {
            existing.UsedAt = DateTime.UtcNow;
        }

        dbContext.MessagingLinkCodes.Add(new MessagingLinkCode
        {
            UserId = userId,
            CodeHash = HashCode(plainCode),
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return (plainCode, expiresAt);
    }

    public async Task<(bool Success, string Message)> LinkAccountAsync(
        MessagingChannel channel,
        string externalId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim();
        if (normalizedCode.Length != CodeLength || !normalizedCode.All(char.IsDigit))
        {
            return (false, "Invalid link code. Generate a new code from Fintraq Settings → Messaging.");
        }

        var linkCode = await dbContext.MessagingLinkCodes
            .Where(c => c.CodeHash == HashCode(normalizedCode) && c.UsedAt == null && c.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (linkCode is null)
        {
            return (false, "Link code expired or invalid. Generate a new code from Fintraq Settings → Messaging.");
        }

        var existingForExternal = await dbContext.MessagingChannelLinks
            .FirstOrDefaultAsync(l => l.Channel == channel && l.ExternalId == externalId, cancellationToken);

        if (existingForExternal is not null)
        {
            if (existingForExternal.UserId != linkCode.UserId)
            {
                return (false, "This chat is already linked to another Fintraq account. Revoke it from that account first.");
            }

            existingForExternal.IsActive = true;
            existingForExternal.LinkedAt = DateTime.UtcNow;
        }
        else
        {
            var existingForUser = await dbContext.MessagingChannelLinks
                .FirstOrDefaultAsync(l => l.UserId == linkCode.UserId && l.Channel == channel, cancellationToken);

            if (existingForUser is not null)
            {
                existingForUser.ExternalId = externalId;
                existingForUser.IsActive = true;
                existingForUser.LinkedAt = DateTime.UtcNow;
            }
            else
            {
                dbContext.MessagingChannelLinks.Add(new MessagingChannelLink
                {
                    UserId = linkCode.UserId,
                    Channel = channel,
                    ExternalId = externalId,
                    LinkedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }
        }

        linkCode.UsedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var channelName = channel == MessagingChannel.Telegram ? "Telegram" : "WhatsApp";
        return (true, $"Fintraq account linked via {channelName}. You can now ask about balance, due items, or log expenses.");
    }

    public async Task<IReadOnlyList<MessagingChannelLink>> GetLinksForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.MessagingChannelLinks
            .Where(l => l.UserId == userId && l.IsActive)
            .OrderBy(l => l.Channel)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> RevokeLinkAsync(string userId, Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await dbContext.MessagingChannelLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.UserId == userId, cancellationToken);

        if (link is null)
        {
            return false;
        }

        link.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string?> ResolveUserIdAsync(MessagingChannel channel, string externalId, CancellationToken cancellationToken = default)
    {
        return await dbContext.MessagingChannelLinks
            .Where(l => l.Channel == channel && l.ExternalId == externalId && l.IsActive)
            .Select(l => l.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string GenerateNumericCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    private static string HashCode(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
