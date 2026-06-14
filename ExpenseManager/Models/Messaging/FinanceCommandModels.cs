using ExpenseManager.Models;

namespace ExpenseManager.Models.Messaging;

public sealed class AddTransactionCommand
{
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionKind Kind { get; set; } = TransactionKind.Expense;
    public ScheduleType ScheduleType { get; set; } = ScheduleType.OneTime;
    public string? CategoryName { get; set; }
    public string? AccountName { get; set; }
    public DateOnly? Date { get; set; }
    public RecurrenceFrequency? Frequency { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public sealed class FinanceCommandResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresClarification { get; set; }
    public Guid? TransactionId { get; set; }
}
