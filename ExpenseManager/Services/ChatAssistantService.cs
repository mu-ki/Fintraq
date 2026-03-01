using System.Runtime.CompilerServices;
using System.Text;
using ExpenseManager.Data;
using ExpenseManager.Models.Chat;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Services;

public sealed class ChatAssistantService(
    ApplicationDbContext dbContext,
    IGeminiService geminiService,
    IFinancialInsightsService financialInsightsService,
    ILogger<ChatAssistantService> logger) : IChatAssistantService
{
    public async Task<ChatQueryResponse> HandleAsync(string userId, ChatQueryRequest request, CancellationToken cancellationToken)
    {
        var userMessage = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return new ChatQueryResponse
            {
                RequiresClarification = true,
                ClarificationQuestion = "Please enter a question."
            };
        }

        await SaveMessageAsync(userId, "user", userMessage, cancellationToken);

        var response = await BuildResponseAsync(userId, userMessage, cancellationToken);
        await SaveMessageAsync(userId, "assistant", response.Reply, cancellationToken);
        return response;
    }

    public async IAsyncEnumerable<ChatStreamChunk> StreamAsync(
        string userId,
        ChatQueryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userMessage = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            yield return new ChatStreamChunk
            {
                Type = "error",
                Content = "Please enter a question."
            };
            yield break;
        }

        await SaveMessageAsync(userId, "user", userMessage, cancellationToken);

        var intent = await geminiService.ExtractIntentAsync(userMessage, DateTime.Now, cancellationToken);
        if (!userMessage.Contains("account", StringComparison.OrdinalIgnoreCase))
        {
            intent.AccountName = null;
        }

        var quickResult = await ValidateIntentAndCollectDataAsync(userId, intent, cancellationToken);
        if (quickResult.earlyReply is not null)
        {
            await SaveMessageAsync(userId, "assistant", quickResult.earlyReply.Reply, cancellationToken);
            yield return new ChatStreamChunk
            {
                Type = "chunk",
                Content = quickResult.earlyReply.Reply
            };
            yield return new ChatStreamChunk
            {
                Type = "done",
                Timestamp = DateTime.Now,
                RequiresClarification = quickResult.earlyReply.RequiresClarification
            };
            yield break;
        }

        var data = quickResult.data!;
        var sb = new StringBuilder();
        try
        {
            await foreach (var textChunk in geminiService.StreamFinancialReplyAsync(
                               userMessage,
                               data.Intent,
                               data.Year!.Value,
                               data.Month!.Value,
                               data.TotalAmount,
                               data.Accounts.Select(a => (a.AccountName, a.Amount)),
                               cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(textChunk))
                {
                    continue;
                }

                sb.Append(textChunk);
                // return new ChatStreamChunk
                // {
                //     Type = "chunk",
                //     Content = textChunk
                // };
                yield break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Streaming reply failed, generating fallback response.");
            var fallback = BuildFallbackReply(data);
            sb.Append(fallback);
            yield break;
            // return new ChatStreamChunk
            // {
            //     Type = "chunk",
            //     Content = fallback
            // };
        }

        var finalText = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(finalText))
        {
            finalText = BuildFallbackReply(data);
        }

        await SaveMessageAsync(userId, "assistant", finalText, cancellationToken);
        yield return new ChatStreamChunk
        {
            Type = "done",
            Timestamp = DateTime.Now,
            RequiresClarification = false
        };
    }

    public async Task<IReadOnlyList<ChatHistoryMessage>> GetHistoryAsync(string userId, CancellationToken cancellationToken)
    {
        var items = await dbContext.ChatMessages
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .Take(200)
            .Select(m => new ChatHistoryMessage
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                Timestamp = m.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task ClearHistoryAsync(string userId, CancellationToken cancellationToken)
    {
        var messages = await dbContext.ChatMessages
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            message.IsDeleted = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ChatQueryResponse> BuildResponseAsync(string userId, string userMessage, CancellationToken cancellationToken)
    {
        var intent = await geminiService.ExtractIntentAsync(userMessage, DateTime.Now, cancellationToken);
        if (!userMessage.Contains("account", StringComparison.OrdinalIgnoreCase))
        {
            intent.AccountName = null;
        }

        var quickResult = await ValidateIntentAndCollectDataAsync(userId, intent, cancellationToken);
        if (quickResult.earlyReply is not null)
        {
            return quickResult.earlyReply;
        }

        var data = quickResult.data!;
        try
        {
            var reply = await geminiService.GenerateFinancialReplyAsync(
                userMessage,
                data.Intent,
                data.Year!.Value,
                data.Month!.Value,
                data.TotalAmount,
                data.Accounts.Select(a => (a.AccountName, a.Amount)),
                cancellationToken);

            return new ChatQueryResponse
            {
                Reply = reply,
                Data = data
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic chat response.");
            return new ChatQueryResponse
            {
                Reply = BuildFallbackReply(data),
                Data = data
            };
        }
    }

    private async Task<(ChatQueryResponse? earlyReply, ChatDataPayload? data)> ValidateIntentAndCollectDataAsync(
        string userId,
        IntentExtractionResult intent,
        CancellationToken cancellationToken)
    {
        if (intent.NeedsClarification)
        {
            return (new ChatQueryResponse
            {
                Reply = intent.ClarificationQuestion ?? "I need more detail to answer.",
                RequiresClarification = true,
                ClarificationQuestion = intent.ClarificationQuestion
            }, null);
        }

        if (intent.Intent is not ("balance" or "income" or "expense"))
        {
            return (new ChatQueryResponse
            {
                Reply = "I can help with balance, income, and expense details. Ask a question like 'What is my balance in March 2026?'.",
                RequiresClarification = true,
                ClarificationQuestion = "Please ask for balance, income, or expense with month and year."
            }, null);
        }

        if (!intent.Month.HasValue || !intent.Year.HasValue)
        {
            return (new ChatQueryResponse
            {
                Reply = "Please provide both month and year.",
                RequiresClarification = true,
                ClarificationQuestion = "Which month and year should I use?"
            }, null);
        }

        FinancialQueryResult result = intent.Intent switch
        {
            "balance" => await financialInsightsService.GetBalanceAsync(userId, intent.Year.Value, intent.Month.Value, intent.AccountName, cancellationToken),
            "income" => await financialInsightsService.GetIncomeAsync(userId, intent.Year.Value, intent.Month.Value, intent.AccountName, cancellationToken),
            "expense" => await financialInsightsService.GetExpenseAsync(userId, intent.Year.Value, intent.Month.Value, intent.AccountName, cancellationToken),
            _ => new FinancialQueryResult()
        };

        if (result.RequiresClarification)
        {
            return (new ChatQueryResponse
            {
                Reply = result.ClarificationQuestion ?? "I need a bit more detail.",
                RequiresClarification = true,
                ClarificationQuestion = result.ClarificationQuestion
            }, null);
        }

        return (null, result.Data);
    }

    private async Task SaveMessageAsync(string userId, string role, string content, CancellationToken cancellationToken)
    {
        dbContext.ChatMessages.Add(new ChatMessage
        {
            UserId = userId,
            Role = role,
            Content = content
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildFallbackReply(ChatDataPayload data)
    {
        var descriptor = data.Intent switch
        {
            "balance" => "balance",
            "income" => "income",
            "expense" => "expense",
            _ => "amount"
        };

        if (data.Accounts.Count == 0)
        {
            return $"No {descriptor} data found for {data.MonthLabel ?? "the selected month"}.";
        }

        var breakdown = string.Join("; ", data.Accounts.Select(a => $"{a.AccountName}: {a.Amount:0.00}"));
        return $"{data.MonthLabel}: total {descriptor} is {data.TotalAmount:0.00}. Breakdown: {breakdown}.";
    }
}
