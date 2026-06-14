using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public interface IMessagingLinkService
{
    Task<(string PlainCode, DateTime ExpiresAt)> GenerateLinkCodeAsync(string userId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Message)> LinkAccountAsync(MessagingChannel channel, string externalId, string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessagingChannelLink>> GetLinksForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeLinkAsync(string userId, Guid linkId, CancellationToken cancellationToken = default);
    Task<string?> ResolveUserIdAsync(MessagingChannel channel, string externalId, CancellationToken cancellationToken = default);
}
