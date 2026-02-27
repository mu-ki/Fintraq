namespace ExpenseManager.Models.ViewModels;

public class DashboardViewModel
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetBalance => TotalIncome - TotalExpense;
    public decimal Savings => NetBalance;
    public List<BankBalanceViewModel> BankBalances { get; set; } = new();
    public List<RecurringDueItemViewModel> RecurringDueItems { get; set; } = new();
    public List<CategoryTotalViewModel> CategoryTotals { get; set; } = new();
    public int CompletedRecurringCount { get; set; }
    public int PendingRecurringCount { get; set; }
    public string TopExpenseCategory { get; set; } = "N/A";
    public decimal HighestDueExpense { get; set; }
}

public class RecurringDueItemViewModel
{
    public Guid TransactionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionKind Kind { get; set; }
    public bool IsCompleted { get; set; }
}

public class BankBalanceViewModel
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public decimal CurrentBalance { get; set; }
    public bool IsManualOverride { get; set; }
}

public class CategoryTotalViewModel
{
    public string CategoryName { get; set; } = string.Empty;
    public TransactionKind Kind { get; set; }
    public decimal Total { get; set; }
}
