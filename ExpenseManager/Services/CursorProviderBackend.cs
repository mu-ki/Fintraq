using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using ExpenseManager.Models.Ai;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public sealed class CursorProviderBackend(
    IAiOptionsProvider optionsProvider,
    IUserContextService userContextService,
    CursorAgentsClient cursorClient,
    ILogger<CursorProviderBackend> logger) : IAiProviderBackend
{
    public AiProvider Provider => AiProvider.Cursor;

    public async Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return AiSharedHelpers.BuildHeuristicIntent(userPrompt, currentDate);
        }

        try
        {
            var prompt = AiSharedHelpers.BuildIntentPrompt(userPrompt, currentDate, conversationContext)
                + "\nReturn ONLY the JSON object.";
            var (raw, _, _) = await CompleteAsync(prompt, cancellationToken);
            var parsed = System.Text.Json.JsonSerializer.Deserialize<IntentExtractionResult>(
                AiSharedHelpers.StripCodeFence(raw),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null)
            {
                return AiSharedHelpers.BuildHeuristicIntent(userPrompt, currentDate);
            }

            parsed.Intent = AiSharedHelpers.NormalizeIntent(parsed.Intent);
            if (parsed.Intent is "balance" or "income" or "expense" or "chit")
            {
                AiSharedHelpers.ApplyClarificationRules(parsed, currentDate);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cursor intent parse failed. Falling back to heuristic parser.");
            return AiSharedHelpers.BuildHeuristicIntent(userPrompt, currentDate);
        }
    }

    public async Task<string> GenerateFinancialReplyAsync(
        string userPrompt,
        string intent,
        int year,
        int month,
        decimal totalAmount,
        IEnumerable<(string AccountName, decimal Amount)> accounts,
        IEnumerable<(string CategoryName, decimal Amount)>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var monthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var categoriesList = categories?.ToList() ?? [];
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return AiSharedHelpers.BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList);
        }

        var accountLines = string.Join("\n", accounts.Select(a => $"- {a.AccountName}: {a.Amount:0.00}"));
        var prompt = $"""
            You are a finance assistant. Reply concisely in plain text.
            Month: {monthLabel}. Intent: {intent}. Total: {totalAmount:0.00}.
            Accounts:
            {accountLines}

            User asked: {userPrompt}
            """;

        try
        {
            var (reply, _, _) = await CompleteAsync(prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(reply)
                ? AiSharedHelpers.BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList)
                : reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cursor financial reply failed.");
            return AiSharedHelpers.BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList);
        }
    }

    public async IAsyncEnumerable<string> StreamFinancialReplyAsync(
        string userPrompt,
        string intent,
        int year,
        int month,
        decimal totalAmount,
        IEnumerable<(string AccountName, decimal Amount)> accounts,
        IEnumerable<(string CategoryName, decimal Amount)>? categories = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reply = await GenerateFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken);
        foreach (var chunk in AiSharedHelpers.ChunkByWords(reply, 4))
        {
            yield return chunk;
        }
    }

    public async Task<string> GenerateChitReplyAsync(string userPrompt, IReadOnlyList<ChitDetailItem> chits, CancellationToken cancellationToken = default)
    {
        if (chits.Count == 0)
        {
            return "You don't have any chits set up.";
        }

        if (!await HasApiKeyAsync(cancellationToken))
        {
            return AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt);
        }

        var chitLines = string.Join("\n", chits.Select(c => $"- {c.Title}: {c.InstallmentAmount:0.00}; completed {c.CompletedCount}"));
        var prompt = $"Answer this chit finance question using the data below.\n{chitLines}\n\nQuestion: {userPrompt}";

        try
        {
            var (reply, _, _) = await CompleteAsync(prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(reply) ? AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt) : reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cursor chit reply failed.");
            return AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt);
        }
    }

    public async Task<string> GenerateOpenEndedReplyAsync(string userPrompt, string userFinancialContext, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return "Configure a Cursor API key in settings for full natural language answers.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("You are a personal finance assistant. Answer using only this data:");
        sb.AppendLine(userFinancialContext);
        if (conversationHistory is { Count: > 0 })
        {
            sb.AppendLine("Recent conversation:");
            foreach (var turn in conversationHistory.TakeLast(8))
            {
                sb.AppendLine($"{turn.Role}: {turn.Content.Trim()}");
            }
        }

        sb.AppendLine($"User asks: {userPrompt}");

        try
        {
            var (reply, _, _) = await CompleteAsync(sb.ToString(), cancellationToken);
            return string.IsNullOrWhiteSpace(reply) ? "I couldn't generate a reply." : reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cursor open-ended reply failed.");
            return "I'm having trouble answering that right now.";
        }
    }

    public async Task<string> GenerateReplyWithToolsAsync(string userId, string userMessage, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return "Cursor API key is not configured. Configure it in Admin → AI settings.";
        }

        var ctx = await userContextService.GetContextAsync(userId, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine(AiSharedHelpers.FinanceToolsSystemPrompt);
        sb.AppendLine("You do not have live tool access in this Cursor agent session. Use the financial context below to answer accurately.");
        sb.AppendLine(ctx.ContextForPrompt);
        if (conversationHistory is { Count: > 0 })
        {
            sb.AppendLine("Recent conversation:");
            foreach (var turn in conversationHistory.TakeLast(8))
            {
                sb.AppendLine($"{turn.Role}: {turn.Content.Trim()}");
            }
        }

        sb.AppendLine($"User message: {userMessage}");

        try
        {
            var (reply, _, _) = await CompleteAsync(sb.ToString(), cancellationToken);
            return reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cursor tool-style reply failed.");
            return "I'm having trouble connecting to Cursor. For faster tool-backed replies, switch to Gemini or Anthropic in Admin settings.";
        }
    }

    private async Task<(string Reply, int PromptTokens, int CompletionTokens)> CompleteAsync(string prompt, CancellationToken cancellationToken)
    {
        var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Cursor, cancellationToken);
        var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Cursor, cancellationToken);
        return await cursorClient.RunPromptAsync(apiKey, model, prompt, cancellationToken);
    }

    private async Task<bool> HasApiKeyAsync(CancellationToken cancellationToken)
        => !string.IsNullOrWhiteSpace(await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Cursor, cancellationToken));
}
