using System.ComponentModel.DataAnnotations;

namespace ExpenseManager.Models;

public class TransactionEntry : AuditableEntity
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal Amount { get; set; }

    public TransactionKind Kind { get; set; }

    public ScheduleType ScheduleType { get; set; }

    public RecurrenceFrequency? Frequency { get; set; }

    public DateOnly? Date { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid? PaidFromAccountId { get; set; }
    public BankAccount? PaidFromAccount { get; set; }

    public Guid? ReceivedToAccountId { get; set; }
    public BankAccount? ReceivedToAccount { get; set; }

    public Guid? ParentTransactionId { get; set; }
    public TransactionEntry? ParentTransaction { get; set; }

    public Guid? RecurrenceGroupId { get; set; }

    public TransactionEntryRole EntryRole { get; set; } = TransactionEntryRole.Standard;

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public ICollection<TransactionEntry> Completions { get; set; } = new List<TransactionEntry>();
}
