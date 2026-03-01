namespace ExpenseManager.Models.Export;

/// <summary>Root DTO for full export/import of bank accounts and transactions.</summary>
public class FinanceDataExport
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; }
    public List<BankAccountExportDto> BankAccounts { get; set; } = [];
    public List<TransactionExportDto> Transactions { get; set; } = [];
}

public class BankAccountExportDto
{
    public Guid Id { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int AccountType { get; set; }
    public decimal InitialBalance { get; set; }
    public bool UseManualOverride { get; set; }
    public decimal? ManualBalanceOverride { get; set; }
}

public class TransactionExportDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Kind { get; set; }
    public int ScheduleType { get; set; }
    public int? Frequency { get; set; }
    public DateOnly? Date { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int CategoryType { get; set; }
    public Guid? PaidFromAccountId { get; set; }
    public Guid? ReceivedToAccountId { get; set; }
    public Guid? ParentTransactionId { get; set; }
    public Guid? RecurrenceGroupId { get; set; }
    public int EntryRole { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
}
