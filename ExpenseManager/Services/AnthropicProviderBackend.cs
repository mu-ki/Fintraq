using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ExpenseManager.Models.Ai;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public sealed class AnthropicProviderBackend(
    IAiOptionsProvider optionsProvider,
    IFinanceToolExecutor toolExecutor,
    IAiTokenUsageService tokenUsage,
    AnthropicRestToolsInvoker restToolsInvoker,
    ILogger<AnthropicProviderBackend> logger) : IAiProviderBackend
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public AiProvider Provider => AiProvider.Anthropic;

    public async Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return AiSharedHelpers.BuildHeuristicIntent(userPrompt, currentDate);
        }

        try
        {
            var prompt = AiSharedHelpers.BuildIntentPrompt(userPrompt, currentDate, conversationContext)
                + "\nReturn ONLY the JSON object, no markdown.";
            var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var raw = await restToolsInvoker.GenerateTextAsync(apiKey, model, prompt, cancellationToken);
            var parsed = JsonSerializer.Deserialize<IntentExtractionResult>(AiSharedHelpers.StripCodeFence(raw), JsonOptions);
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
            logger.LogWarning(ex, "Anthropic intent parse failed. Falling back to heuristic parser.");
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
        var categoryLines = intent == "expense" && categoriesList.Count > 0
            ? "\nCategory breakdown:\n" + string.Join("\n", categoriesList.Select(c => $"- {c.CategoryName}: {c.Amount:0.00}"))
            : string.Empty;

        var prompt = $"""
            You are a finance assistant. Keep response concise and exact.
            Month: {monthLabel}. Intent: {intent}. Total: {totalAmount:0.00}.
            Accounts:
            {accountLines}
            {categoryLines}

            User asked: {userPrompt}
            Reply in plain text only.
            """;

        try
        {
            var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var response = await restToolsInvoker.GenerateTextAsync(apiKey, model, prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(response)
                ? AiSharedHelpers.BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList)
                : response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Anthropic financial reply failed.");
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
            return "You don't have any chits set up, or no chits match your question.";
        }

        if (!await HasApiKeyAsync(cancellationToken))
        {
            return AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt);
        }

        var chitLines = string.Join("\n", chits.Select(c =>
            $"- {c.Title}: amount {c.InstallmentAmount:0.00}; completed {c.CompletedCount}" +
            (c.TotalInstallments.HasValue ? $" of {c.TotalInstallments}" : " (ongoing)")));

        var prompt = $"""
            You are a finance assistant. Answer using the chit data below. Be concise.
            Chits:
            {chitLines}

            User asked: {userPrompt}
            """;

        try
        {
            var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var response = await restToolsInvoker.GenerateTextAsync(apiKey, model, prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(response) ? AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt) : response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Anthropic chit reply failed.");
            return AiSharedHelpers.BuildChitFallbackReply(chits, userPrompt);
        }
    }

    public async Task<string> GenerateOpenEndedReplyAsync(string userPrompt, string userFinancialContext, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return "Configure an Anthropic API key in settings for full natural language answers.";
        }

        var historyBlock = conversationHistory is { Count: > 0 }
            ? "\nRecent conversation:\n" + string.Join("\n", conversationHistory.TakeLast(8).Select(t => $"{t.Role}: {t.Content.Trim()}"))
            : string.Empty;

        var prompt = $"""
            You are a helpful personal finance assistant. Use only the user's data below.
            {userFinancialContext}
            {historyBlock}

            User asks: {userPrompt}
            Reply in plain text only.
            """;

        try
        {
            var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Anthropic, cancellationToken);
            var response = await restToolsInvoker.GenerateTextAsync(apiKey, model, prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(response)
                ? "I couldn't generate a reply. Try asking about balance, income, expenses, or chits."
                : response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Anthropic open-ended reply failed.");
            return "I'm having trouble answering that right now.";
        }
    }

    public async Task<string> GenerateReplyWithToolsAsync(string userId, string userMessage, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
    {
        if (!await HasApiKeyAsync(cancellationToken))
        {
            return "Anthropic API key is not configured. Configure it in Admin → AI settings.";
        }

        var apiKey = await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken);
        var model = await optionsProvider.GetModelForProviderAsync(AiProvider.Anthropic, cancellationToken);
        var messages = BuildInitialMessages(userMessage, conversationHistory);

        try
        {
            var (reply, promptTokens, completionTokens) = await restToolsInvoker.GenerateWithToolsAsync(
                apiKey, model, messages, toolExecutor, userId, cancellationToken);
            try
            {
                await tokenUsage.RecordAsync(userId, $"anthropic:{model}", promptTokens, completionTokens, cancellationToken);
            }
            catch
            {
                // best effort
            }

            return reply;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Anthropic tool calling failed.");
            return "I'm having trouble connecting to Anthropic. Try again or switch provider in Admin settings.";
        }
    }

    private static List<AnthropicMessage> BuildInitialMessages(string userMessage, IReadOnlyList<ChatTurn>? conversationHistory)
    {
        var messages = new List<AnthropicMessage>();
        if (conversationHistory is { Count: > 0 })
        {
            foreach (var turn in conversationHistory.TakeLast(10))
            {
                var role = string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
                messages.Add(new AnthropicMessage { Role = role, Content = turn.Content.Trim() });
            }
        }

        messages.Add(new AnthropicMessage { Role = "user", Content = userMessage.Trim() });
        return messages;
    }

    private async Task<bool> HasApiKeyAsync(CancellationToken cancellationToken)
        => !string.IsNullOrWhiteSpace(await optionsProvider.GetApiKeyForProviderAsync(AiProvider.Anthropic, cancellationToken));
}
