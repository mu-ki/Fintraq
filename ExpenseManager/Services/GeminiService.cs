using System.Globalization;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using ExpenseManager.Configuration;
using ExpenseManager.Models.Chat;
using Microsoft.Extensions.Options;

namespace ExpenseManager.Services;

public sealed class GeminiService(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiService> logger) : IGeminiService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GeminiOptions _options = options.Value;

    public Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, CancellationToken cancellationToken)
    {
        return ExtractIntentAsync(userPrompt, currentDate, null, cancellationToken);
    }

    public async Task<IntentExtractionResult> ExtractIntentAsync(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext, CancellationToken cancellationToken)
    {
        if (!HasApiKey())
        {
            return BuildHeuristicIntent(userPrompt, currentDate);
        }

        var contextBlock = "";
        if (conversationContext is { Count: > 0 })
        {
            var lines = conversationContext.TakeLast(10).Select(t => $"{t.Role}: {t.Content.Trim()}");
            contextBlock = $@"
Recent conversation (use this to resolve ""that month"", ""same"", ""yes"", ""march"", etc.):
{string.Join("\n", lines)}

";
        }

        var prompt = $@"
You are an intent parser for a personal finance assistant. Be helpful and infer intent from minimal or incomplete user input.
Today is {currentDate:yyyy-MM-dd}.
{contextBlock}Return ONLY valid JSON with this exact shape:
{{
  ""intent"": ""balance|income|expense|chit|other"",
  ""month"": 1-12 or null,
  ""year"": yyyy or null,
  ""accountName"": ""string or null"",
  ""needsClarification"": true/false,
  ""clarificationQuestion"": ""string or null""
}}

Rules:
- Resolve relative dates: ""this month"", ""last month"", ""current month"" -> use today's month/year.
- For very short or minimal queries (e.g. ""balance"", ""income"", ""expense"", ""march"", ""march balance"") infer intent and, when no date is given, default to current month and year (set month and year from today) so the user gets an answer without being asked. Set needsClarification=false in that case.
- If the user asks about chit installments (e.g. ""how many installments completed"", ""installment amount"", ""Thiyagu Chit"", ""Thiya Mama Chit"", ""how much installment"", ""chit completed"") set intent to ""chit"". No month/year needed for chit.
- If the user says ""that month"", ""same"", ""yes"" or refers to a month/year mentioned in the recent conversation, use that month/year.
- Only set needsClarification=true when the user explicitly asks something ambiguous that cannot be inferred from context (e.g. ""which year?"" when multiple years were discussed).
- If month is present but year is missing, use current year. If only year is given, set needsClarification=true and ask which month.
- If query is not about income/expense/balance/chit, set intent to ""other"".

Current user message: {userPrompt}
";

        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    responseMimeType = "application/json"
                }
            };

            var raw = await GenerateContentAsync(payload, cancellationToken);
            var cleaned = StripCodeFence(raw);
            var parsed = JsonSerializer.Deserialize<IntentExtractionResult>(cleaned, JsonOptions);
            if (parsed is null)
            {
                return BuildHeuristicIntent(userPrompt, currentDate);
            }

            parsed.Intent = NormalizeIntent(parsed.Intent);
            if (parsed.Intent is "balance" or "income" or "expense" or "chit")
            {
                ApplyClarificationRules(parsed, currentDate);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini intent parse failed. Falling back to heuristic parser.");
            return BuildHeuristicIntent(userPrompt, currentDate);
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
        var accountLines = string.Join("\n", accounts.Select(a => $"- {a.AccountName}: {a.Amount:0.00}"));
        if (string.IsNullOrWhiteSpace(accountLines))
        {
            accountLines = "- No account entries";
        }

        var categoryLines = "";
        var categoriesList = categories?.ToList() ?? new List<(string CategoryName, decimal Amount)>();
        if (intent == "expense" && categoriesList.Count > 0)
        {
            categoryLines = "\nCategory breakdown (use this to answer questions about specific categories like Chit Fund, Food, etc.):\n"
                + string.Join("\n", categoriesList.Select(c => $"- {c.CategoryName}: {c.Amount:0.00}"));
        }

        if (!HasApiKey())
        {
            return BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList);
        }

        var prompt = $"""
            You are a finance assistant for a personal app.
            Keep response concise and exact.
            Mention month as {monthLabel}.
            Intent: {intent}
            Total amount: {totalAmount:0.00}
            Account breakdown:
            {accountLines}
            {categoryLines}

            User asked: {userPrompt}
            Respond with plain text only.
            """;

        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2
                }
            };

            var response = await GenerateContentAsync(payload, cancellationToken);
            return string.IsNullOrWhiteSpace(response)
                ? BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList)
                : response.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini reply generation failed. Falling back to deterministic response.");
            return BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList);
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
        var monthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        var accountLines = string.Join("\n", accounts.Select(a => $"- {a.AccountName}: {a.Amount:0.00}"));
        if (string.IsNullOrWhiteSpace(accountLines))
        {
            accountLines = "- No account entries";
        }

        var categoriesList = categories?.ToList() ?? new List<(string CategoryName, decimal Amount)>();
        var categoryLines = "";
        if (intent == "expense" && categoriesList.Count > 0)
        {
            categoryLines = "\nCategory breakdown (use this to answer questions about specific categories like Chit Fund, Food, etc.):\n"
                + string.Join("\n", categoriesList.Select(c => $"- {c.CategoryName}: {c.Amount:0.00}"));
        }

        if (!HasApiKey())
        {
            var fallback = BuildDeterministicReply(intent, monthLabel, totalAmount, accounts, categoriesList);
            foreach (var chunk in ChunkByWords(fallback, 4))
            {
                yield return chunk;
            }

            yield break;
        }

        var prompt = $"""
            You are a finance assistant for a personal app.
            Keep response concise and exact.
            Mention month as {monthLabel}.
            Intent: {intent}
            Total amount: {totalAmount:0.00}
            Account breakdown:
            {accountLines}
            {categoryLines}

            User asked: {userPrompt}
            Respond with plain text only.
            """;

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2
            }
        };

        var endpoint = BuildGeminiEndpoint(stream: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Gemini stream request failed. Falling back to non-stream response.");
            var fallback = await GenerateFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken);
            foreach (var chunk in ChunkByWords(fallback, 4))
            {
                // yield return chunk;
            }

            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("Gemini stream response failed with status {Status}: {Body}", (int)response.StatusCode, body);
                var fallback = await GenerateFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken);
                foreach (var chunk in ChunkByWords(fallback, 4))
                {
                    yield return chunk;
                }

                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new StringBuilder();

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var payloadLine = line["data:".Length..].Trim();
                    if (payloadLine == "[DONE]")
                    {
                        break;
                    }

                    var text = ExtractTextFromStreamEvent(payloadLine);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        buffer.Append(text);
                        yield return text;
                    }
                }
            }

            if (buffer.Length == 0)
            {
                var fallback = await GenerateFinancialReplyAsync(userPrompt, intent, year, month, totalAmount, accounts, categories, cancellationToken);
                foreach (var chunk in ChunkByWords(fallback, 4))
                {
                    yield return chunk;
                }
            }
        }
    }

    public async Task<string> GenerateChitReplyAsync(string userPrompt, IReadOnlyList<ChitDetailItem> chits, CancellationToken cancellationToken = default)
    {
        if (chits.Count == 0)
        {
            return "You don't have any chits (Chit Fund recurring expenses) set up, or no chits match your question. Add a recurring expense with category \"Chit Fund\" to track installments.";
        }

        var chitLines = new StringBuilder();
        foreach (var c in chits)
        {
            var totalStr = c.TotalInstallments.HasValue ? $"{c.TotalInstallments} total" : "ongoing";
            chitLines.AppendLine($"- {c.Title}: Installment amount {c.InstallmentAmount:0.00}; Completed: {c.CompletedCount} ({totalStr}); Start: {c.StartDate ?? "—"}; End: {c.EndDate ?? "—"}; Frequency: {c.FrequencyLabel ?? "—"}");
        }

        if (!HasApiKey())
        {
            return BuildChitFallbackReply(chits, userPrompt);
        }

        var focusInstruction = chits.Count == 1
            ? "The user asked about one specific chit. Answer only about that chit with the exact numbers below."
            : "If the user asked about a specific chit by name, answer ONLY for that chit. Do not list other chits.";

        var prompt = $"""
            You are a finance assistant. The user has the following chit(s). Use this data to answer their question precisely.
            {focusInstruction}

            Chit details:
            {chitLines}

            User asked: {userPrompt}

            Answer in plain text. Be concise. For "how many installments" give the completed count (and total if known). For "installment amount" give the amount. Do not list chits the user did not ask about.
            """;

        try
        {
            var payload = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.2 }
            };
            var response = await GenerateContentAsync(payload, cancellationToken);
            return string.IsNullOrWhiteSpace(response) ? BuildChitFallbackReply(chits, userPrompt) : response.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chit reply generation failed. Using fallback.");
            return BuildChitFallbackReply(chits, userPrompt);
        }
    }

    private static string BuildChitFallbackReply(IReadOnlyList<ChitDetailItem> chits, string? userPrompt = null)
    {
        if (chits.Count == 1)
        {
            var c = chits[0];
            var totalStr = c.TotalInstallments.HasValue ? $"{c.CompletedCount} of {c.TotalInstallments} installments completed" : $"{c.CompletedCount} installments completed (ongoing)";
            return $"{c.Title}: {totalStr}. Installment amount: {c.InstallmentAmount:0.00}.";
        }
        var lines = chits.Select(c =>
        {
            var totalStr = c.TotalInstallments.HasValue ? $"{c.CompletedCount} of {c.TotalInstallments}" : $"{c.CompletedCount} completed (ongoing)";
            return $"{c.Title}: {totalStr} installments, amount {c.InstallmentAmount:0.00} per installment";
        });
        return "Chit details:\n" + string.Join("\n", lines);
    }

    public async Task<string> GenerateOpenEndedReplyAsync(string userPrompt, string userFinancialContext, IReadOnlyList<ChatTurn>? conversationHistory, CancellationToken cancellationToken = default)
    {
        var contextBlock = string.IsNullOrWhiteSpace(userFinancialContext)
            ? " (No account or transaction data available yet.)"
            : "\n\n" + userFinancialContext;

        var historyBlock = "";
        if (conversationHistory is { Count: > 0 })
        {
            var lines = conversationHistory.TakeLast(8).Select(t => $"{t.Role}: {t.Content.Trim()}");
            historyBlock = "\n\nRecent conversation:\n" + string.Join("\n", lines);
        }

        var prompt = $"""
            You are a helpful personal finance assistant. You have full knowledge of the current user's financial data below. Answer their question in natural language using only this data. Be concise and accurate. If the data does not contain what they ask, say so politely. Do not make up numbers or accounts.
            User's financial data:
            {contextBlock}
            {historyBlock}

            User now asks: {userPrompt}
            Reply in plain text only.
            """;

        if (!HasApiKey())
        {
            return "I can answer questions about your balance, income, expenses, chits, and recent transactions. Enable the Gemini API key in settings for full natural language answers.";
        }

        try
        {
            var payload = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.2 }
            };
            var response = await GenerateContentAsync(payload, cancellationToken);
            return string.IsNullOrWhiteSpace(response) ? "I couldn't generate a reply. Try asking about your balance, income, expenses, or chits." : response.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Open-ended reply generation failed.");
            return "I'm having trouble answering that right now. You can ask things like: balance this month, income last month, expenses by category, or chit installment status.";
        }
    }

    private async Task<string> GenerateContentAsync(object payload, CancellationToken cancellationToken)
    {
        if (!HasApiKey())
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var endpoint = BuildGeminiEndpoint(stream: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini API call failed with status {(int)response.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
        {
            return string.Empty;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textPart))
            {
                return textPart.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string StripCodeFence(string input)
    {
        var trimmed = input.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('\n');
        var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return trimmed.Trim('`');
        }

        return trimmed.Substring(start + 1, end - start - 1).Trim();
    }

    private static string NormalizeIntent(string? intent)
    {
        return intent?.Trim().ToLowerInvariant() switch
        {
            "balance" => "balance",
            "income" => "income",
            "expense" => "expense",
            "chit" => "chit",
            _ => "other"
        };
    }

    private static void ApplyClarificationRules(IntentExtractionResult result, DateTime currentDate)
    {
        // Default to current month/year when both missing so minimal queries like "balance" get an answer
        if (!result.Month.HasValue && !result.Year.HasValue)
        {
            result.Month = currentDate.Month;
            result.Year = currentDate.Year;
            result.NeedsClarification = false;
            result.ClarificationQuestion = null;
            return;
        }

        if (result.Month.HasValue && !result.Year.HasValue)
        {
            result.Year = currentDate.Year;
            result.NeedsClarification = false;
            result.ClarificationQuestion = null;
            return;
        }

        if (!result.Month.HasValue && result.Year.HasValue)
        {
            result.NeedsClarification = true;
            result.ClarificationQuestion ??= $"Which month in {result.Year.Value} should I use?";
        }
    }

    private static IntentExtractionResult BuildHeuristicIntent(string userPrompt, DateTime currentDate)
    {
        var lower = userPrompt.ToLowerInvariant();
        var intent = "other";
        if (lower.Contains("balance", StringComparison.Ordinal))
        {
            intent = "balance";
        }
        else if (lower.Contains("income", StringComparison.Ordinal) || lower.Contains("earn", StringComparison.Ordinal))
        {
            intent = "income";
        }
        else if (lower.Contains("expense", StringComparison.Ordinal) || lower.Contains("spent", StringComparison.Ordinal) || lower.Contains("spend", StringComparison.Ordinal))
        {
            intent = "expense";
        }
        else if (lower.Contains("chit", StringComparison.Ordinal) || lower.Contains("installment", StringComparison.Ordinal))
        {
            intent = "chit";
        }

        int? month = null;
        int? year = null;
        string? accountName = null;

        if (lower.Contains("this month", StringComparison.Ordinal))
        {
            month = currentDate.Month;
            year = currentDate.Year;
        }
        else if (lower.Contains("last month", StringComparison.Ordinal))
        {
            var d = currentDate.AddMonths(-1);
            month = d.Month;
            year = d.Year;
        }

        if (!month.HasValue)
        {
            var monthNames = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
            for (var i = 0; i < 12; i++)
            {
                var m = monthNames[i];
                if (string.IsNullOrWhiteSpace(m))
                {
                    continue;
                }

                if (Regex.IsMatch(lower, $@"\b{Regex.Escape(m.ToLowerInvariant())}\b"))
                {
                    month = i + 1;
                    break;
                }
            }
        }

        var yearMatch = Regex.Match(lower, @"\b(19|20)\d{2}\b");
        if (yearMatch.Success && int.TryParse(yearMatch.Value, out var parsedYear))
        {
            year = parsedYear;
        }

        var accountMatch = Regex.Match(userPrompt, @"(?:account|bank)\s+([A-Za-z0-9 _-]{2,50})", RegexOptions.IgnoreCase);
        if (accountMatch.Success)
        {
            accountName = accountMatch.Groups[1].Value.Trim();
        }

        var result = new IntentExtractionResult
        {
            Intent = intent,
            Month = month,
            Year = year,
            AccountName = accountName
        };

        if (intent is "balance" or "income" or "expense")
        {
            ApplyClarificationRules(result, currentDate);
        }

        return result;
    }

    private bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(_options.ApiKey);
    }

    private static string BuildDeterministicReply(
        string intent,
        string monthLabel,
        decimal totalAmount,
        IEnumerable<(string AccountName, decimal Amount)> accounts,
        IReadOnlyList<(string CategoryName, decimal Amount)>? categories = null)
    {
        var noun = intent switch
        {
            "balance" => "balance",
            "income" => "income",
            "expense" => "expense",
            _ => "value"
        };

        var lines = accounts.Select(a => $"{a.AccountName}: {a.Amount:0.00}").ToList();
        if (lines.Count == 0 && (categories == null || categories.Count == 0))
        {
            return $"No {noun} data found for {monthLabel}.";
        }

        var breakdown = string.Join("; ", lines);
        if (intent == "expense" && categories is { Count: > 0 })
        {
            var catBreakdown = string.Join("; ", categories.Select(c => $"{c.CategoryName}: {c.Amount:0.00}"));
            breakdown = string.IsNullOrEmpty(breakdown) ? catBreakdown : $"{breakdown}. By category: {catBreakdown}";
        }
        return $"{monthLabel} {noun} total is {totalAmount:0.00}. Breakdown: {breakdown}.";
    }

    private string BuildGeminiEndpoint(bool stream)
    {
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-2.0-flash" : _options.Model.Trim();
        var action = stream ? "streamGenerateContent" : "generateContent";
        var streamQuery = stream ? "&alt=sse" : string.Empty;
        return $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:{action}?key={_options.ApiKey.Trim()}{streamQuery}";
    }

    private static string ExtractTextFromStreamEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var first = candidates[0];
            if (!first.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
            {
                return string.Empty;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textPart))
                {
                    return textPart.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            return string.Empty;
        }

        return string.Empty;
    }

    private static IEnumerable<string> ChunkByWords(string text, int wordsPerChunk)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i += wordsPerChunk)
        {
            var take = Math.Min(wordsPerChunk, words.Length - i);
            var chunk = string.Join(" ", words.Skip(i).Take(take));
            if (i + take < words.Length)
            {
                chunk += " ";
            }

            yield return chunk;
        }
    }
}
