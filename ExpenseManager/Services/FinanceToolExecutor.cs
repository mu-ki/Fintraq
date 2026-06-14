using System.Globalization;
using System.Text.Json;
using ExpenseManager.Models;
using ExpenseManager.Models.Chat;
using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public sealed class FinanceToolExecutor(
    IFinancialInsightsService financialInsightsService,
    IUserContextService userContextService,
    IFinanceCommandService financeCommandService) : IFinanceToolExecutor
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
                "add_transaction" => await AddTransactionAsync(userId, root, cancellationToken),
                "list_due_items" => await CommandResultAsync(financeCommandService.ListDueItemsAsync(userId, GetOptionalInt(root, "year"), GetOptionalInt(root, "month"), cancellationToken)),
                "mark_due_done" => await CommandResultAsync(financeCommandService.MarkDueDoneAsync(userId, GetStringOrNull(root, "titleSearch") ?? string.Empty, GetOptionalInt(root, "year"), GetOptionalInt(root, "month"), cancellationToken)),
                "revert_due" => await CommandResultAsync(financeCommandService.RevertDueAsync(userId, GetStringOrNull(root, "titleSearch") ?? string.Empty, GetOptionalInt(root, "year"), GetOptionalInt(root, "month"), cancellationToken)),
                "add_bank_account" => await AddBankAccountAsync(userId, root, cancellationToken),
                "list_accounts" => await CommandResultAsync(financeCommandService.ListAccountsAsync(userId, cancellationToken)),
                "list_categories" => await ListCategoriesAsync(userId, root, cancellationToken),
                "delete_transaction" => await CommandResultAsync(financeCommandService.DeleteTransactionAsync(userId, GetStringOrNull(root, "titleSearch") ?? string.Empty, cancellationToken)),
                "get_month_summary" => await CommandResultAsync(financeCommandService.GetMonthSummaryAsync(userId, GetOptionalInt(root, "year"), GetOptionalInt(root, "month"), cancellationToken)),
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

    private static int? GetOptionalInt(JsonElement e, string name)
    {
        var value = GetIntOrNull(e, name);
        return value is not null && int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static decimal? GetDecimalOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
        return p.TryGetDecimal(out var d) ? d : null;
    }

    private static string? GetStringOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        return p.GetString();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private static async Task<string> CommandResultAsync(Task<FinanceCommandResult> task)
    {
        var result = await task;
        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            message = result.Message,
            clarification = result.RequiresClarification ? result.Message : null,
            transactionId = result.TransactionId
        });
    }

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

    private async Task<string> AddTransactionAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var title = GetStringOrNull(args, "title");
        var amount = GetDecimalOrNull(args, "amount");
        var kindText = GetStringOrNull(args, "kind");
        if (string.IsNullOrWhiteSpace(title) || !amount.HasValue || string.IsNullOrWhiteSpace(kindText))
        {
            return "{\"error\": \"title, amount, and kind are required\"}";
        }

        if (!Enum.TryParse<TransactionKind>(kindText, true, out var kind))
        {
            return "{\"error\": \"kind must be Income or Expense\"}";
        }

        ScheduleType scheduleType = ScheduleType.OneTime;
        var scheduleTypeText = GetStringOrNull(args, "scheduleType");
        if (!string.IsNullOrWhiteSpace(scheduleTypeText) &&
            !Enum.TryParse<ScheduleType>(scheduleTypeText, true, out scheduleType))
        {
            return "{\"error\": \"scheduleType must be OneTime or Recurring\"}";
        }

        RecurrenceFrequency? frequency = null;
        var frequencyText = GetStringOrNull(args, "frequency");
        if (!string.IsNullOrWhiteSpace(frequencyText) &&
            Enum.TryParse<RecurrenceFrequency>(frequencyText, true, out var parsedFrequency))
        {
            frequency = parsedFrequency;
        }

        DateOnly? date = null;
        var dateText = GetStringOrNull(args, "date");
        if (!string.IsNullOrWhiteSpace(dateText) &&
            DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate;
        }

        var result = await financeCommandService.AddTransactionAsync(userId, new AddTransactionCommand
        {
            Title = title,
            Amount = amount.Value,
            Kind = kind,
            CategoryName = GetStringOrNull(args, "categoryName"),
            AccountName = GetStringOrNull(args, "accountName"),
            Date = date,
            ScheduleType = scheduleType,
            Frequency = frequency
        }, ct);

        return JsonSerializer.Serialize(new
        {
            success = result.Success,
            message = result.Message,
            clarification = result.RequiresClarification ? result.Message : null,
            transactionId = result.TransactionId
        });
    }

    private async Task<string> AddBankAccountAsync(string userId, JsonElement args, CancellationToken ct)
    {
        var accountName = GetStringOrNull(args, "accountName");
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return "{\"error\": \"accountName is required\"}";
        }

        var accountType = AccountType.Savings;
        var accountTypeText = GetStringOrNull(args, "accountType");
        if (!string.IsNullOrWhiteSpace(accountTypeText))
        {
            Enum.TryParse<AccountType>(accountTypeText, true, out accountType);
        }

        var initialBalance = GetDecimalOrNull(args, "initialBalance") ?? 0m;
        return await CommandResultAsync(financeCommandService.AddBankAccountAsync(userId, accountName, accountType, initialBalance, ct));
    }

    private async Task<string> ListCategoriesAsync(string userId, JsonElement args, CancellationToken ct)
    {
        CategoryType? type = null;
        var typeText = GetStringOrNull(args, "type");
        if (!string.IsNullOrWhiteSpace(typeText) && Enum.TryParse<CategoryType>(typeText, true, out var parsedType))
        {
            type = parsedType;
        }

        return await CommandResultAsync(financeCommandService.ListCategoriesAsync(userId, type, ct));
    }
}
