using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public interface IChatAssistantService
{
    Task<ChatQueryResponse> HandleAsync(string userId, ChatQueryRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ChatStreamChunk> StreamAsync(string userId, ChatQueryRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatHistoryMessage>> GetHistoryAsync(string userId, CancellationToken cancellationToken);
    Task ClearHistoryAsync(string userId, CancellationToken cancellationToken);
}
