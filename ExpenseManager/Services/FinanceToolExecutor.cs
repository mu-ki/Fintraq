using System.Globalization;
using System.Text.Json;
using ExpenseManager.Models.Chat;

namespace ExpenseManager.Services;

public sealed class FinanceToolExecutor(
    IFinancialInsightsService financialInsightsService,
    IUserContextService userContextService) : IFinanceToolExecutor
{
    public async Task<string> ExecuteAsync(string userId, string functionName, string argsJson, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
        var root = doc.RootElement;

        try
        {
            return functionName switch
            {
                "get_balance" => await GetBalanceAsync(userId, root, cancellationToken),
                "get_income" => await GetIncomeAsync(userId, root, cancellationToken),
                "get_expense" => await GetExpenseAsync(userId, root, cancellationToken),
                "get_chit_details" => await GetChitDetailsAsync(userId, root, cancellationToken),
                "get_financial_summary" => await GetFinancialSummaryAsync(userId, cancellationToken),
                _ => $"{{\"error\": \"Unknown function: {functionName}\"}}"
            };
        }
        catch (Exception ex)
        {
            return $"{{\"error\": \"{Escape(ex.Message)}\"}}";
        }
    }

    private static string? GetIntOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
        return p.TryGetInt32(out var i) ? i.ToString() : null;
    }

    private static string? GetStringOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        return p.GetString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private async Task<string> GetBalanceAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var y = GetIntOrNull(args, "year");
        var m = GetIntOrNull(args, "month");
        if (y is null || m is null || !int.TryParse(y, out var year) || !int.TryParse(m, out var month))
            return "{\"error\": \"year and month are required (integers)\"}";
        var accountName = GetStringOrNull(args, "accountName");
        var result = await financialInsightsService.GetBalanceAsync(userId, year, month, accountName, ct);
        if (result.RequiresClarification)
            return JsonSerializer.Serialize(new { clarification = result.ClarificationQuestion });
        var data = result.Data!;
        var accounts = data.Accounts.Select(a => new { a.AccountName, a.Amount }).ToList();
        return JsonSerializer.Serialize(new { data.MonthLabel, data.TotalAmount, accounts });
    }

    private async Task<string> GetIncomeAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var y = GetIntOrNull(args, "year"); var m = GetIntOrNull(args, "month");
        if (y is null || m is null || !int.TryParse(y, out var year) || !int.TryParse(m, out var month))
            return "{\"error\": \"year and month are required\"}";
        var accountName = GetStringOrNull(args, "accountName");
        var result = await financialInsightsService.GetIncomeAsync(userId, year, month, accountName, ct);
        if (result.RequiresClarification)
            return JsonSerializer.Serialize(new { clarification = result.ClarificationQuestion });
        var data = result.Data!;
        return JsonSerializer.Serialize(new { data.MonthLabel, data.TotalAmount, accounts = data.Accounts.Select(a => new { a.AccountName, a.Amount }) });
    }

    private async Task<string> GetExpenseAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var y = GetIntOrNull(args, "year"); var m = GetIntOrNull(args, "month");
        if (y is null || m is null || !int.TryParse(y, out var year) || !int.TryParse(m, out var month))
            return "{\"error\": \"year and month are required\"}";
        var accountName = GetStringOrNull(args, "accountName");
        var result = await financialInsightsService.GetExpenseAsync(userId, year, month, accountName, ct);
        if (result.RequiresClarification)
            return JsonSerializer.Serialize(new { clarification = result.ClarificationQuestion });
        var data = result.Data!;
        return JsonSerializer.Serialize(new
        {
            data.MonthLabel,
            data.TotalAmount,
            accounts = data.Accounts.Select(a => new { a.AccountName, a.Amount }),
            categories = data.Categories.Select(c => new { c.CategoryName, c.Amount })
        });
    }

    private async Task<string> GetChitDetailsAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var chitName = GetStringOrNull(args, "chitName");
        var result = await financialInsightsService.GetChitDetailsAsync(userId, ct);
        var chits = result.Data?.Chits ?? new List<ChitDetailItem>();
        if (!string.IsNullOrWhiteSpace(chitName))
        {
            var filtered = chits.Where(c =>
                c.Title.IndexOf(chitName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (chitName.Length >= 4 && c.Title.IndexOf(chitName, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (filtered.Count == 0)
                return JsonSerializer.Serialize(new
                {
                    message = $"No chit found matching '{chitName}'.",
                    availableChits = chits.Select(c => c.Title).ToList()
                });
            chits = filtered;
        }
        var list = chits.Select(c => new
        {
            c.Title,
            c.InstallmentAmount,
            c.CompletedCount,
            c.TotalInstallments,
            c.StartDate,
            c.EndDate,
            c.FrequencyLabel
        }).ToList();
        return JsonSerializer.Serialize(new { chits = list });
    }

    private async Task<string> GetFinancialSummaryAsync(string userId, CancellationToken ct)
    {
        var ctx = await userContextService.GetContextAsync(userId, ct);
        return JsonSerializer.Serialize(new { summary = ctx.ContextForPrompt, currentMonth = ctx.CurrentMonth, currentYear = ctx.CurrentYear });
    }
}
