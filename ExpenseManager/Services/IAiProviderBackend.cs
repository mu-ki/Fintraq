using ExpenseManager.Models.Ai;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public interface IAiProviderBackend
{
    AiProvider Provider { get; }
    Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken);
    Task<string> GenerateFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, IEnumerable<(string CategoryName, decimal Amount)>? categories = null, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, IEnumerable<(string CategoryName, decimal Amount)>? categories = null, CancellationToken cancellationToken = default);
    Task<string> GenerateChitReplyAsync(string userPrompt, IReadOnlyList<ChitDetailItem> chits, CancellationToken cancellationToken = default);
    Task<string> GenerateOpenEndedReplyAsync(string userPrompt, string userFinancialContext, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default);
    Task<string> GenerateReplyWithToolsAsync(string userId, string userMessage, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default);
}
