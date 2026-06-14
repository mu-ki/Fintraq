using System.Text.RegularExpressions;
using ExpenseManager.Models;
using ExpenseManager.Models.Chat;
using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public sealed partial class MessagingOrchestrator(
    IMessagingLinkService linkService,
    IChatAssistantService chatAssistantService,
    IFinanceCommandService financeCommandService,
    IFinancialInsightsService financialInsightsService,
    ITelegramBotClient telegramBotClient,
    IWhatsAppCloudClient whatsAppCloudClient,
    ILogger<MessagingOrchestrator> logger) : IMessagingOrchestrator
{
    private const string UnlinkedMessage =
        "Your chat is not linked to Fintraq yet. Log in at fintraq.runasp.net, open Settings → Messaging, generate a code, then send: /link YOUR_CODE";

    [GeneratedRegex(@"^(?:spent|paid|expense)\s+(\d+(?:\.\d+)?)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpentCommandRegex();

    [GeneratedRegex(@"^income\s+(\d+(?:\.\d+)?)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IncomeCommandRegex();

    [GeneratedRegex(@"^mark\s+(.+?)\s+done$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkDoneCommandRegex();

    public async Task HandleInboundAsync(MessagingChannel channel, string externalId, string text, CancellationToken cancellationToken = default)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (TryParseLinkCommand(trimmed, out var linkCode))
        {
            var linkResult = await linkService.LinkAccountAsync(channel, externalId, linkCode, cancellationToken);
            await SendReplyAsync(channel, externalId, linkResult.Message, cancellationToken);
            return;
        }

        var userId = await linkService.ResolveUserIdAsync(channel, externalId, cancellationToken);
        if (userId is null)
        {
            await SendReplyAsync(channel, externalId, UnlinkedMessage, cancellationToken);
            return;
        }

        try
        {
            var quickReply = await TryQuickCommandAsync(userId, trimmed, cancellationToken);
            if (quickReply is not null)
            {
                await SendReplyAsync(channel, externalId, quickReply, cancellationToken);
                return;
            }

            var response = await chatAssistantService.HandleAsync(userId, new ChatQueryRequest { Message = trimmed }, cancellationToken);
            var reply = response.RequiresClarification && !string.IsNullOrWhiteSpace(response.ClarificationQuestion)
                ? response.ClarificationQuestion
                : response.Reply;

            await SendReplyAsync(channel, externalId, reply, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle inbound message for {Channel} {ExternalId}", channel, externalId);
            await SendReplyAsync(channel, externalId, "Sorry, something went wrong processing your message. Please try again.", cancellationToken);
        }
    }

    private async Task<string?> TryQuickCommandAsync(string userId, string text, CancellationToken cancellationToken)
    {
        var normalized = text.Trim();
        var lower = normalized.ToLowerInvariant();
        var now = DateTime.Now;

        if (lower is "balance" or "bal")
        {
            var result = await financialInsightsService.GetBalanceAsync(userId, now.Year, now.Month, null, cancellationToken);
            if (result.RequiresClarification)
            {
                return result.ClarificationQuestion;
            }

            var data = result.Data;
            if (data.Accounts.Count == 0)
            {
                return $"{data.MonthLabel} balance: {FormatMoney(data.TotalAmount)}";
            }

            var accounts = string.Join("\n", data.Accounts.Select(a => $"• {a.AccountName}: {FormatMoney(a.Amount)}"));
            return $"{data.MonthLabel} balance: {FormatMoney(data.TotalAmount)}\n{accounts}";
        }

        if (lower is "due" or "dues")
        {
            var result = await financeCommandService.ListDueItemsAsync(userId, cancellationToken: cancellationToken);
            return result.Message;
        }

        if (lower is "summary" or "month")
        {
            var result = await financeCommandService.GetMonthSummaryAsync(userId, cancellationToken: cancellationToken);
            return result.Message;
        }

        if (lower is "accounts" or "account")
        {
            var result = await financeCommandService.ListAccountsAsync(userId, cancellationToken);
            return result.Message;
        }

        if (lower is "categories" or "category")
        {
            var result = await financeCommandService.ListCategoriesAsync(userId, cancellationToken: cancellationToken);
            return result.Message;
        }

        if (lower.StartsWith("income ", StringComparison.OrdinalIgnoreCase))
        {
            var result = await financialInsightsService.GetIncomeAsync(userId, now.Year, now.Month, null, cancellationToken);
            if (result.RequiresClarification)
            {
                return result.ClarificationQuestion;
            }

            return $"{result.Data.MonthLabel} income: {FormatMoney(result.Data.TotalAmount)}";
        }

        if (lower.StartsWith("expense", StringComparison.OrdinalIgnoreCase) ||
            lower.StartsWith("expenses", StringComparison.OrdinalIgnoreCase))
        {
            var result = await financialInsightsService.GetExpenseAsync(userId, now.Year, now.Month, null, cancellationToken);
            if (result.RequiresClarification)
            {
                return result.ClarificationQuestion;
            }

            return $"{result.Data.MonthLabel} expenses: {FormatMoney(result.Data.TotalAmount)}";
        }

        var spentMatch = SpentCommandRegex().Match(normalized);
        if (spentMatch.Success)
        {
            return await HandleQuickExpenseAsync(userId, spentMatch.Groups[1].Value, spentMatch.Groups[2].Value, cancellationToken);
        }

        var incomeMatch = IncomeCommandRegex().Match(normalized);
        if (incomeMatch.Success)
        {
            return await HandleQuickIncomeAsync(userId, incomeMatch.Groups[1].Value, incomeMatch.Groups[2].Value, cancellationToken);
        }

        var markDoneMatch = MarkDoneCommandRegex().Match(normalized);
        if (markDoneMatch.Success)
        {
            var result = await financeCommandService.MarkDueDoneAsync(userId, markDoneMatch.Groups[1].Value.Trim(), cancellationToken: cancellationToken);
            return result.Message;
        }

        return null;
    }

    private async Task<string> HandleQuickExpenseAsync(string userId, string amountText, string remainder, CancellationToken cancellationToken)
    {
        if (!decimal.TryParse(amountText, out var amount) || amount <= 0)
        {
            return "Could not parse expense amount.";
        }

        var (title, categoryName, accountName) = ParseExpenseRemainder(remainder);
        var result = await financeCommandService.AddTransactionAsync(userId, new AddTransactionCommand
        {
            Title = title,
            Amount = amount,
            Kind = TransactionKind.Expense,
            CategoryName = categoryName,
            AccountName = accountName,
            Date = DateOnly.FromDateTime(DateTime.Now)
        }, cancellationToken);

        if (!result.Success)
        {
            return result.Message;
        }

        var summary = await financeCommandService.GetMonthSummaryAsync(userId, cancellationToken: cancellationToken);
        return $"{result.Message}\n{summary.Message.Split('\n').FirstOrDefault()}";
    }

    private async Task<string> HandleQuickIncomeAsync(string userId, string amountText, string remainder, CancellationToken cancellationToken)
    {
        if (!decimal.TryParse(amountText, out var amount) || amount <= 0)
        {
            return "Could not parse income amount.";
        }

        var (title, categoryName, accountName) = ParseExpenseRemainder(remainder);
        var result = await financeCommandService.AddTransactionAsync(userId, new AddTransactionCommand
        {
            Title = title,
            Amount = amount,
            Kind = TransactionKind.Income,
            CategoryName = categoryName,
            AccountName = accountName,
            Date = DateOnly.FromDateTime(DateTime.Now)
        }, cancellationToken);

        return result.Message;
    }

    private static (string Title, string? CategoryName, string? AccountName) ParseExpenseRemainder(string remainder)
    {
        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("Expense", null, null);
        }

        if (parts.Length == 1)
        {
            return (Capitalize(parts[0]), parts[0], null);
        }

        var accountName = parts[^1];
        var categoryName = parts[0];
        var titleParts = parts.Length > 2 ? parts[1..^1] : Array.Empty<string>();
        var title = titleParts.Length > 0 ? string.Join(' ', titleParts) : Capitalize(categoryName);
        return (Capitalize(title), categoryName, accountName);
    }

    private static bool TryParseLinkCommand(string text, out string code)
    {
        code = string.Empty;
        if (text.StartsWith("/link ", StringComparison.OrdinalIgnoreCase))
        {
            code = text[6..].Trim();
            return code.Length > 0;
        }

        if (text.StartsWith("/start link_", StringComparison.OrdinalIgnoreCase))
        {
            code = text["/start link_".Length..].Trim();
            return code.Length > 0;
        }

        return false;
    }

    private async Task SendReplyAsync(MessagingChannel channel, string externalId, string text, CancellationToken cancellationToken)
    {
        var formatted = MessagingReplyFormatter.Format(channel, text);
        if (channel == MessagingChannel.Telegram)
        {
            await telegramBotClient.SendTextMessageAsync(
                externalId,
                formatted.Text,
                formatted.ParseMode,
                formatted.PlainFallback,
                cancellationToken);
        }
        else
        {
            await whatsAppCloudClient.SendTextMessageAsync(externalId, formatted.Text, cancellationToken);
        }
    }

    private static string Capitalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string FormatMoney(decimal amount) =>
        amount.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}
