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
            MakeSdkDeclaration("get_financial_summary", "Get full financial summary: accounts and balances, recent transactions, recurring items, chits, monthly income/expense summary. Use for open-ended questions like 'summary', 'overview', 'recent transactions', or when other tools do not fit.")
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
            new { name = "get_financial_summary", description = "Get full financial summary: accounts and balances, recent transactions, recurring items, chits, monthly income/expense summary. Use for open-ended questions like 'summary', 'overview', 'recent transactions', or when other tools do not fit.", parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() } }
        ];
    }
}
