namespace ExpenseManager.Models.Chat;

public sealed class ChatQueryResponse
{
    public string Reply { get; set; } = string.Empty;
    public bool RequiresClarification { get; set; }
    public string? ClarificationQuestion { get; set; }
    public ChatDataPayload? Data { get; set; }
}

public sealed class ChatDataPayload
{
    public string Intent { get; set; } = string.Empty;
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? MonthLabel { get; set; }
    public List<AccountAmountItem> Accounts { get; set; } = new();
    /// <summary>Expense breakdown by category (e.g. Chit Fund, Food). Only set when Intent is "expense".</summary>
    public List<CategoryAmountItem> Categories { get; set; } = new();
    /// <summary>Chit/recurring installment details. Only set when Intent is "chit".</summary>
    public List<ChitDetailItem> Chits { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string CurrencyCode { get; set; } = "INR";
}

public sealed class AccountAmountItem
{
    public Guid? AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool? IsManualOverride { get; set; }
}

public sealed class CategoryAmountItem
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class ChitDetailItem
{
    public string Title { get; set; } = string.Empty;
    public decimal InstallmentAmount { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? FrequencyLabel { get; set; }
    public int CompletedCount { get; set; }
    public int? TotalInstallments { get; set; }
}
