using ExpenseManager.Models.Ai;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public sealed class AiAssistantService(
    IAiOptionsProvider optionsProvider,
    GeminiService geminiBackend,
    AnthropicProviderBackend anthropicBackend,
    CursorProviderBackend cursorBackend) : IGeminiService
{
    public async Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, CancellationToken cancellationToken)
        => await (await ResolveAsync(cancellationToken)).ExtractIntentAsync(userPrompt, currentDate, null, cancellationToken);

    public async Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken)
        => await (await ResolveAsync(cancellationToken)).ExtractIntentAsync(userPrompt, currentDate, conversationContext, cancellationToken);

    public async Task<string> GenerateFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, IEnumerable<(string CategoryName, decimal Amount)>? categories = null, CancellationToken cancellationToken = default)
        => await (await ResolveAsync(cancellationToken)).GenerateFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken);

    public async IAsyncEnumerable<string> StreamFinancialReplyAsync(string userPrompt, string intent, int year, int month, decimal totalAmount, IEnumerable<(string AccountName, decimal Amount)> accounts, IEnumerable<(string CategoryName, decimal Amount)>? categories = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var backend = await ResolveAsync(cancellationToken);
        await foreach (var chunk in backend.StreamFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken))
        {
            yield return chunk;
        }
    }

    public async Task<string> GenerateChitReplyAsync(string userPrompt, IReadOnlyList<ChitDetailItem> chits, CancellationToken cancellationToken = default)
        => await (await ResolveAsync(cancellationToken)).GenerateChitReplyAsync(userPrompt, chits, cancellationToken);

    public async Task<string> GenerateOpenEndedReplyAsync(string userPrompt, string userFinancialContext, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
        => await (await ResolveAsync(cancellationToken)).GenerateOpenEndedReplyAsync(userPrompt, userFinancialContext, conversationHistory, cancellationToken);

    public async Task<string> GenerateReplyWithToolsAsync(string userId, string userMessage, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
        => await (await ResolveAsync(cancellationToken)).GenerateReplyWithToolsAsync(userId, userMessage, conversationHistory, cancellationToken);

    private async Task<IAiProviderBackend> ResolveAsync(CancellationToken cancellationToken)
    {
        var provider = await optionsProvider.GetProviderAsync(cancellationToken);
        return provider switch
        {
            AiProvider.Anthropic => anthropicBackend,
            AiProvider.Cursor => cursorBackend,
            _ => geminiBackend
        };
    }
}
