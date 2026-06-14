using System.Globalization;
using System.Text.RegularExpressions;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

internal static class AiSharedHelpers
{
    internal static string StripCodeFence(string input)
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

    internal static string NormalizeIntent(string? intent) =>
        intent?.Trim().ToLowerInvariant() switch
        {
            "balance" => "balance",
            "income" => "income",
            "expense" => "expense",
            "chit" => "chit",
            _ => "other"
        };

    internal static void ApplyClarificationRules(IntentExtractionResult result, DateTime currentDate)
    {
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

    internal static IntentExtractionResult BuildHeuristicIntent(string userPrompt, DateTime currentDate)
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
        else
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
                    year = currentDate.Year;
                    break;
                }
            }
        }

        return new IntentExtractionResult
        {
            Intent = intent,
            Month = month,
            Year = year,
            NeedsClarification = false
        };
    }

    internal static string BuildDeterministicReply(
        string intent,
        string monthLabel,
        decimal totalAmount,
        IEnumerable<(string AccountName, decimal Amount)> accounts,
        IReadOnlyList<(string CategoryName, decimal Amount)>? categories = null)
    {
        var noun = intent switch { "balance" => "balance", "income" => "income", "expense" => "expense", _ => "value" };
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

    internal static string BuildChitFallbackReply(IReadOnlyList<ChitDetailItem> chits, string? userPrompt = null)
    {
        if (chits.Count == 1)
        {
            var c = chits[0];
            var totalStr = c.TotalInstallments.HasValue
                ? $"{c.CompletedCount} of {c.TotalInstallments} installments completed"
                : $"{c.CompletedCount} installments completed (ongoing)";
            return $"{c.Title}: {totalStr}. Installment amount: {c.InstallmentAmount:0.00}.";
        }

        var lines = chits.Select(c =>
        {
            var totalStr = c.TotalInstallments.HasValue
                ? $"{c.CompletedCount} of {c.TotalInstallments}"
                : $"{c.CompletedCount} completed (ongoing)";
            return $"{c.Title}: {totalStr} installments, amount {c.InstallmentAmount:0.00} per installment";
        });
        return "Chit details:\n" + string.Join("\n", lines);
    }

    internal static IEnumerable<string> ChunkByWords(string text, int wordsPerChunk)
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

    internal static string BuildIntentPrompt(string userPrompt, DateTime currentDate, IReadOnlyList<ChatTurn>? conversationContext)
    {
        var contextBlock = "";
        if (conversationContext is { Count: > 0 })
        {
            var lines = conversationContext.TakeLast(10).Select(t => $"{t.Role}: {t.Content.Trim()}");
            contextBlock = $@"
Recent conversation (use this to resolve ""that month"", ""same"", ""yes"", ""march"", etc.):
{string.Join("\n", lines)}

";
        }

        return $@"
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
- If the user asks about chit installments set intent to ""chit"". No month/year needed for chit.
- If the user says ""that month"", ""same"", ""yes"" or refers to a month/year mentioned in the recent conversation, use that month/year.
- Only set needsClarification=true when the user explicitly asks something ambiguous that cannot be inferred from context.
- If month is present but year is missing, use current year. If only year is given, set needsClarification=true and ask which month.
- If query is not about income/expense/balance/chit, set intent to ""other"".

Current user message: {userPrompt}
";
    }

    internal const string FinanceToolsSystemPrompt = """
        You are a personal finance assistant. Use the provided tools to fetch the user's balance, income, expenses, chit (Chit Fund) details, or full financial summary.
        When the user does not specify a month or year, use the current month and year.
        For chit questions, call get_chit_details with the chit name they mention; if no chit matches, tell them and list available chits.
        You can also add transactions, mark due items done, list accounts, and manage finances via the write tools when the user asks.
        Reply in natural language based on the tool results. Be concise and accurate.
        """;
}
