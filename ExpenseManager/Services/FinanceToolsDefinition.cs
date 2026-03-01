using Google.GenAI.Types;

namespace ExpenseManager.Services;

/// <summary>
/// Defines the tools (function declarations) exposed to the AI so it can call app APIs instead of relying on intent parsing.
/// </summary>
public static class FinanceToolsDefinition
{
    public static IReadOnlyList<Tool> GetTools()
    {
        var declarations = new List<FunctionDeclaration>
        {
            new FunctionDeclaration
            {
                Name = "get_balance",
                Description = "Get the user's account balance for a given month. Use when the user asks about balance, total money, or how much they have.",
                ParametersJsonSchema = """
                {
                    "type": "object",
                    "properties": {
                        "year": { "type": "integer", "description": "Year (e.g. 2026)" },
                        "month": { "type": "integer", "description": "Month 1-12" },
                        "accountName": { "type": "string", "description": "Optional bank account name to filter" }
                    },
                    "required": ["year", "month"]
                }
                """
            },
            new FunctionDeclaration
            {
                Name = "get_income",
                Description = "Get the user's income for a given month. Use when the user asks about income or earnings.",
                ParametersJsonSchema = """
                {
                    "type": "object",
                    "properties": {
                        "year": { "type": "integer", "description": "Year" },
                        "month": { "type": "integer", "description": "Month 1-12" },
                        "accountName": { "type": "string", "description": "Optional account name" }
                    },
                    "required": ["year", "month"]
                }
                """
            },
            new FunctionDeclaration
            {
                Name = "get_expense",
                Description = "Get the user's expenses for a given month, including breakdown by account and by category (e.g. Chit Fund, Food). Use when the user asks about spending, expenses, or what they spent.",
                ParametersJsonSchema = """
                {
                    "type": "object",
                    "properties": {
                        "year": { "type": "integer", "description": "Year" },
                        "month": { "type": "integer", "description": "Month 1-12" },
                        "accountName": { "type": "string", "description": "Optional account name" }
                    },
                    "required": ["year", "month"]
                }
                """
            },
            new FunctionDeclaration
            {
                Name = "get_chit_details",
                Description = "Get chit (Chit Fund) installment details: name, installment amount, how many installments completed, total installments. Use when the user asks about chits, installments, Thiyagu Chit, Thiya Mama Chit, or any chit by name. Pass chitName to get only that chit (partial name match, e.g. 'Thiyagu' or 'Thiya Mama').",
                ParametersJsonSchema = """
                {
                    "type": "object",
                    "properties": {
                        "chitName": { "type": "string", "description": "Optional. Filter by chit name (e.g. Thiyagu, Thiya Mama). If omitted, returns all chits." }
                    }
                }
                """
            },
            new FunctionDeclaration
            {
                Name = "get_financial_summary",
                Description = "Get full financial summary: accounts and balances, recent transactions, recurring items, chits, monthly income/expense summary. Use for open-ended questions like 'summary', 'overview', 'recent transactions', or when other tools do not fit.",
                ParametersJsonSchema = """
                {
                    "type": "object",
                    "properties": {}
                }
                """
            }
        };

        return new List<Tool>
        {
            new Tool { FunctionDeclarations = declarations }
        };
    }
}
