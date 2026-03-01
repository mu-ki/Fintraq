namespace ExpenseManager.Services;

public interface IGeminiService
{
    Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, CancellationToken cancellationToken);
    Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken);
    Task<string> GenerateFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, CancellationToken cancellationToken);
}

public sealed class IntentExtractionResult
{
    public string Intent { get; set; } = "other";
    public int? Month { get; set; }
    public int? Year { get; set; }
    public string? AccountName { get; set; }
    public bool NeedsClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
}

public sealed class ChatTurn
{
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
}
