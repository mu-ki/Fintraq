using System.ComponentModel.DataAnnotations;

namespace ExpenseManager.Models.ViewModels;

public class TransactionCreateViewModel
{
    [Required(ErrorMessage = "Title is required.")]
    [MaxLength(200)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Amount")]
    [Required(ErrorMessage = "Amount is required.")]
    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal Amount { get; set; }

    [Display(Name = "Category")]
    [Required(ErrorMessage = "Category is required.")]
    public Guid? CategoryId { get; set; }

    [Display(Name = "Type")]
    [Required]
    public TransactionKind Kind { get; set; } = TransactionKind.Expense;

    [Display(Name = "Schedule")]
    [Required]
    public ScheduleType ScheduleType { get; set; } = ScheduleType.OneTime;

    [Display(Name = "Frequency")]
    public RecurrenceFrequency? Frequency { get; set; }

    [Display(Name = "Date")]
    public DateOnly? Date { get; set; }
    [Display(Name = "Start Date")]
    public DateOnly? StartDate { get; set; }
    [Display(Name = "End Date")]
    public DateOnly? EndDate { get; set; }

    [Display(Name = "Paid From Account")]
    public Guid? PaidFromAccountId { get; set; }
    [Display(Name = "Received To Account")]
    public Guid? ReceivedToAccountId { get; set; }

    public List<Category> Categories { get; set; } = new();
    public List<BankAccount> Accounts { get; set; } = new();
}

public class RecurringUpdateFutureViewModel
{
    public Guid TransactionId { get; set; }

    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal NewAmount { get; set; }

    [Required]
    public DateOnly EffectiveFrom { get; set; }
}

public class TransactionEditDatesViewModel
{
    public Guid TransactionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ScheduleType ScheduleType { get; set; }
    public RecurrenceFrequency? Frequency { get; set; }

    [Display(Name = "Date")]
    public DateOnly? Date { get; set; }

    [Display(Name = "Start Date")]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "End Date")]
    public DateOnly? EndDate { get; set; }
}

public class MarkCompletionViewModel
{
    public Guid TransactionId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Title { get; set; } = string.Empty;
    public TransactionKind Kind { get; set; }

    [Display(Name = "Amount")]
    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal Amount { get; set; }
}

public class TransactionListViewModel
{
    public List<TransactionEntry> ActiveTransactions { get; set; } = new();
    public List<TransactionEntry> CompletedTransactions { get; set; } = new();
}

public class TransactionEditAmountViewModel
{
    public Guid TransactionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ScheduleType ScheduleType { get; set; }

    [Display(Name = "Amount")]
    [Range(typeof(decimal), "0.01", "999999999999.99")]
    public decimal Amount { get; set; }
}
