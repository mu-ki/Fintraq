using System.Text.Json;
using Google.GenAI.Types;

namespace ExpenseManager.Services;
/// <summary>
/// Defines the tools (function declarations) exposed to the AI so it can call app APIs instead of relying on intent parsing.
/// Gemini API expects "parameters" to be a JSON object; the SDK sends ParametersJsonSchema as a string and the API returns
/// "schema at top-level must be a boolean or an object". We use REST with GetToolsForRestApi() so parameters is sent as object.
/// </summary>
public static class FinanceToolsDefinition
{
    /// <summary>For SDK usage (may trigger schema error on some API versions).</summary>
    public static IReadOnlyList<Tool> GetTools()
    {
        var declarations = new List<FunctionDeclaration>
        {
            MakeSdkDeclaration("get_balance", "Get the user's account balance for a given month. Use when the user asks about balance, total money, or how much they have.", ("year", "integer", true), ("month", "integer", true), ("accountName", "string", false)),
            MakeSdkDeclaration("get_income", "Get the user's income for a given month. Use when the user asks about income or earnings.", ("year", "integer", true), ("month", "integer", true), ("accountName", "string", false)),
            MakeSdkDeclaration("get_expense", "Get the user's expenses for a given month, including breakdown by account and by category (e.g. Chit Fund, Food). Use when the user asks about spending, expenses, or what they spent.", ("year", "integer", true), ("month", "integer", true), ("accountName", "string", false)),
            MakeSdkDeclaration("get_chit_details", "Get chit (Chit Fund) installment details: name, installment amount, how many installments completed, total installments. Use when the user asks about chits, installments, Thiyagu Chit, Thiya Mama Chit, or any chit by name. Pass chitName to get only that chit (partial name match, e.g. 'Thiyagu' or 'Thiya Mama').", ("chitName", "string", false)),
            MakeSdkDeclaration("get_financial_summary", "Get full financial summary: accounts and balances, recent transactions, recurring items, chits, monthly income/expense summary. Use for open-ended questions like 'summary', 'overview', 'recent transactions', or when other tools do not fit."),
            MakeSdkDeclaration("add_transaction", "Create a one-time or recurring income/expense transaction. Expenses require accountName (paid from).", ("title", "string", true), ("amount", "number", true), ("kind", "string", true), ("categoryName", "string", false), ("accountName", "string", false), ("date", "string", false), ("scheduleType", "string", false), ("frequency", "string", false)),
            MakeSdkDeclaration("list_due_items", "List pending due items for a month (recurring and one-time).", ("year", "integer", false), ("month", "integer", false)),
            MakeSdkDeclaration("mark_due_done", "Mark a due item as completed for the current or specified month. Match by partial title.", ("titleSearch", "string", true), ("year", "integer", false), ("month", "integer", false)),
            MakeSdkDeclaration("revert_due", "Revert a completed due item for a month.", ("titleSearch", "string", true), ("year", "integer", false), ("month", "integer", false)),
            MakeSdkDeclaration("add_bank_account", "Create a new bank account.", ("accountName", "string", true), ("accountType", "string", false), ("initialBalance", "number", false)),
            MakeSdkDeclaration("list_accounts", "List user bank accounts with current balances."),
            MakeSdkDeclaration("list_categories", "List available categories, optionally filtered by type (Income or Expense).", ("type", "string", false)),
            MakeSdkDeclaration("delete_transaction", "Soft-delete a transaction by partial title match.", ("titleSearch", "string", true)),
            MakeSdkDeclaration("get_month_summary", "Get month income, expense, net, and account balances.", ("year", "integer", false), ("month", "integer", false))
        };
        return new List<Tool> { new Tool { FunctionDeclarations = declarations } };
    }

    private static FunctionDeclaration MakeSdkDeclaration(string name, string description, params (string name, string type, bool required)[] props)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var (pname, ptype, req) in props)
        {
            properties[pname] = new Dictionary<string, object> { ["type"] = ptype, ["description"] = pname };
            if (req) required.Add(pname);
        }
        var schema = new Dictionary<string, object> { ["type"] = "object", ["properties"] = properties, ["required"] = required };
        return new FunctionDeclaration { Name = name, Description = description, ParametersJsonSchema = JsonSerializer.Serialize(schema) };
    }

    /// <summary>Tools as REST API payload: functionDeclarations with "parameters" as object (fixes schema validation error).</summary>
    public static IReadOnlyList<object> GetToolsForRestApi()
    {
        return
        [
            new { name = "get_balance", description = "Get the user's account balance for a given month. Use when the user asks about balance, total money, or how much they have.", parameters = new { type = "object", properties = new { year = new { type = "integer", description = "Year (e.g. 2026)" }, month = new { type = "integer", description = "Month 1-12" }, accountName = new { type = "string", description = "Optional bank account name to filter" } }, required = new[] { "year", "month" } } },
            new { name = "get_income", description = "Get the user's income for a given month. Use when the user asks about income or earnings.", parameters = new { type = "object", properties = new { year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month 1-12" }, accountName = new { type = "string", description = "Optional account name" } }, required = new[] { "year", "month" } } },
            new { name = "get_expense", description = "Get the user's expenses for a given month, including breakdown by account and by category (e.g. Chit Fund, Food). Use when the user asks about spending, expenses, or what they spent.", parameters = new { type = "object", properties = new { year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month 1-12" }, accountName = new { type = "string", description = "Optional account name" } }, required = new[] { "year", "month" } } },
            new { name = "get_chit_details", description = "Get chit (Chit Fund) installment details: name, installment amount, how many installments completed, total installments. Use when the user asks about chits, installments, Thiyagu Chit, Thiya Mama Chit, or any chit by name. Pass chitName to get only that chit (partial name match, e.g. 'Thiyagu' or 'Thiya Mama').", parameters = new { type = "object", properties = new { chitName = new { type = "string", description = "Optional. Filter by chit name (e.g. Thiyagu, Thiya Mama). If omitted, returns all chits." } }, required = Array.Empty<string>() } },
            new { name = "get_financial_summary", description = "Get full financial summary: accounts and balances, recent transactions, recurring items, chits, monthly income/expense summary. Use for open-ended questions like 'summary', 'overview', 'recent transactions', or when other tools do not fit.", parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() } },
            new { name = "add_transaction", description = "Create a one-time or recurring income/expense. Expenses require accountName.", parameters = new { type = "object", properties = new { title = new { type = "string", description = "Transaction title" }, amount = new { type = "number", description = "Amount" }, kind = new { type = "string", description = "Income or Expense" }, categoryName = new { type = "string", description = "Category name" }, accountName = new { type = "string", description = "Bank account name" }, date = new { type = "string", description = "ISO date yyyy-MM-dd for one-time" }, scheduleType = new { type = "string", description = "OneTime or Recurring" }, frequency = new { type = "string", description = "Weekly, Monthly, etc." } }, required = new[] { "title", "amount", "kind" } } },
            new { name = "list_due_items", description = "List pending due items for a month.", parameters = new { type = "object", properties = new { year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month 1-12" } }, required = Array.Empty<string>() } },
            new { name = "mark_due_done", description = "Mark a due item complete by partial title match.", parameters = new { type = "object", properties = new { titleSearch = new { type = "string", description = "Partial title" }, year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month" } }, required = new[] { "titleSearch" } } },
            new { name = "revert_due", description = "Revert a completed due item.", parameters = new { type = "object", properties = new { titleSearch = new { type = "string", description = "Partial title" }, year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month" } }, required = new[] { "titleSearch" } } },
            new { name = "add_bank_account", description = "Create a bank account.", parameters = new { type = "object", properties = new { accountName = new { type = "string", description = "Account name" }, accountType = new { type = "string", description = "Savings, Current, etc." }, initialBalance = new { type = "number", description = "Initial balance" } }, required = new[] { "accountName" } } },
            new { name = "list_accounts", description = "List bank accounts with balances.", parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() } },
            new { name = "list_categories", description = "List categories.", parameters = new { type = "object", properties = new { type = new { type = "string", description = "Income or Expense" } }, required = Array.Empty<string>() } },
            new { name = "delete_transaction", description = "Delete transaction by partial title.", parameters = new { type = "object", properties = new { titleSearch = new { type = "string", description = "Partial title" } }, required = new[] { "titleSearch" } } },
            new { name = "get_month_summary", description = "Month income, expense, net, balances.", parameters = new { type = "object", properties = new { year = new { type = "integer", description = "Year" }, month = new { type = "integer", description = "Month" } }, required = Array.Empty<string>() } }
        ];
    }

    /// <summary>Anthropic Messages API tool definitions with input_schema.</summary>
    public static IReadOnlyList<object> GetToolsForAnthropicApi()
    {
        var result = new List<object>();
        foreach (var tool in GetToolsForRestApi())
        {
            var json = JsonSerializer.Serialize(tool);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            result.Add(new Dictionary<string, object>
            {
                ["name"] = root.GetProperty("name").GetString()!,
                ["description"] = root.GetProperty("description").GetString()!,
                ["input_schema"] = JsonSerializer.Deserialize<object>(root.GetProperty("parameters").GetRawText())!
            });
        }

        return result;
    }
}
