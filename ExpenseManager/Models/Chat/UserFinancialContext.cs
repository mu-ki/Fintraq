namespace ExpenseManager.Models.Chat;

/// <summary>
/// Full financial context for the current user, built for the AI to answer open-ended questions and perform reasoning.
/// </summary>
public sealed class UserFinancialContext
{
    /// <summary>Human-readable summary to inject into the AI system prompt. Contains accounts, balances, recent transactions, recurring items, chits, and monthly summaries.</summary>
    public string ContextForPrompt { get; set; } = string.Empty;

    /// <summary>Current month (1-12) and year used for "this month" summaries.</summary>
    public int CurrentMonth { get; set; }
    public int CurrentYear { get; set; }
}
