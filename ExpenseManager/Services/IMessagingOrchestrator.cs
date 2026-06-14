using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public interface IMessagingOrchestrator
{
    Task HandleInboundAsync(MessagingChannel channel, string externalId, string text, CancellationToken cancellationToken = default);
}
