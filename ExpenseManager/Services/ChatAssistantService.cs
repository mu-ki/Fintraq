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

        var response = await BuildResponseAsync(userId, userMessage, cancellationToken);
        await SaveMessageAsync(userId, "user", userMessage, cancellationToken);
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

        var recentHistory = await GetRecentHistoryForContextAsync(userId, maxTurns: 10, cancellationToken);
        var intent = await geminiService.ExtractIntentAsync(userMessage, DateTime.Now, recentHistory, cancellationToken);
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
            if (data.Intent == "chit")
            {
                var chitReply = await geminiService.GenerateChitReplyAsync(userMessage, data.Chits, cancellationToken);
                sb.Append(chitReply);
            }
            else
            {
                await foreach (var textChunk in geminiService.StreamFinancialReplyAsync(
                                   userMessage,
                                   data.Intent,
                                   data.Year!.Value,
                                   data.Month!.Value,
                                   data.TotalAmount,
                                   data.Accounts.Select(a => (a.AccountName, a.Amount)),
                                   data.Intent == "expense" && data.Categories.Count > 0 ? data.Categories.Select(c => (c.CategoryName, c.Amount)) : null,
                                   cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(textChunk))
                    {
                        continue;
                    }

                    sb.Append(textChunk);
                    yield break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Streaming reply failed, generating fallback response.");
            sb.Append(BuildFallbackReply(data));
            yield break;
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
        var recentHistory = await GetRecentHistoryForContextAsync(userId, maxTurns: 10, cancellationToken);
        var intent = await geminiService.ExtractIntentAsync(userMessage, DateTime.Now, recentHistory, cancellationToken);
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
            string reply;
            if (data.Intent == "chit")
            {
                reply = await geminiService.GenerateChitReplyAsync(userMessage, data.Chits, cancellationToken);
            }
            else
            {
                reply = await geminiService.GenerateFinancialReplyAsync(
                    userMessage,
                    data.Intent,
                    data.Year!.Value,
                    data.Month!.Value,
                    data.TotalAmount,
                    data.Accounts.Select(a => (a.AccountName, a.Amount)),
                    data.Intent == "expense" && data.Categories.Count > 0 ? data.Categories.Select(c => (c.CategoryName, c.Amount)) : null,
                    cancellationToken);
            }

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

        if (intent.Intent is not ("balance" or "income" or "expense" or "chit"))
        {
            return (new ChatQueryResponse
            {
                Reply = "I can help with balance, income, expense, and chit installment details. Ask a question like 'What is my balance in March 2026?' or 'How many installments completed in Thiyagu Chit?'.",
                RequiresClarification = true,
                ClarificationQuestion = "Please ask for balance, income, expense, or chit installments."
            }, null);
        }

        if (intent.Intent == "chit")
        {
            var chitResult = await financialInsightsService.GetChitDetailsAsync(userId, cancellationToken);
            return (null, chitResult.Data);
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

    private async Task<IReadOnlyList<ChatTurn>?> GetRecentHistoryForContextAsync(string userId, int maxTurns, CancellationToken cancellationToken)
    {
        var messages = await dbContext.ChatMessages
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxTurns * 2)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return null;
        }

        return messages
            .AsEnumerable()
            .Reverse()
            .Select(m => new ChatTurn { Role = m.Role ?? "user", Content = m.Content ?? string.Empty })
            .ToList();
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
        if (data.Intent == "chit")
        {
            if (data.Chits.Count == 0)
            {
                return "No chits (Chit Fund recurring expenses) found. Add a recurring expense with category \"Chit Fund\" to track installments.";
            }
            var lines = data.Chits.Select(c =>
            {
                var totalStr = c.TotalInstallments.HasValue ? $"{c.CompletedCount} of {c.TotalInstallments}" : $"{c.CompletedCount} completed (ongoing)";
                return $"{c.Title}: installment {c.InstallmentAmount:0.00}, {totalStr}";
            });
            return "Chit details: " + string.Join("; ", lines);
        }

        var descriptor = data.Intent switch
        {
            "balance" => "balance",
            "income" => "income",
            "expense" => "expense",
            _ => "amount"
        };

        if (data.Accounts.Count == 0 && (data.Categories == null || data.Categories.Count == 0))
        {
            return $"No {descriptor} data found for {data.MonthLabel ?? "the selected month"}.";
        }

        var breakdown = string.Join("; ", data.Accounts.Select(a => $"{a.AccountName}: {a.Amount:0.00}"));
        if (data.Intent == "expense" && data.Categories.Count > 0)
        {
            var catBreakdown = string.Join("; ", data.Categories.Select(c => $"{c.CategoryName}: {c.Amount:0.00}"));
            breakdown = string.IsNullOrEmpty(breakdown) ? catBreakdown : $"{breakdown}. By category: {catBreakdown}";
        }
        return $"{data.MonthLabel}: total {descriptor} is {data.TotalAmount:0.00}. Breakdown: {breakdown}.";
    }
}
