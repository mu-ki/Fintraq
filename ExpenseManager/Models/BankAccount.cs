using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ExpenseManager.Models;

public class BankAccount : AuditableEntity
{
    [BindNever]
    public string UserId { get; set; } = string.Empty;

    [Display(Name = "Account Name")]
    [Required(ErrorMessage = "Account Name is required.")]
    [MaxLength(100)]
    public string AccountName { get; set; } = string.Empty;

    [Display(Name = "Account Type")]
    public AccountType AccountType { get; set; }

    [Display(Name = "Initial Balance")]
    [Range(typeof(decimal), "0", "999999999999.99", ErrorMessage = "Initial Balance must be zero or positive.")]
    public decimal InitialBalance { get; set; }

    [Display(Name = "Use Manual Override")]
    public bool UseManualOverride { get; set; }

    [Display(Name = "Manual Balance Override")]
    public decimal? ManualBalanceOverride { get; set; }

    public ICollection<TransactionEntry> PaidTransactions { get; set; } = new List<TransactionEntry>();
    public ICollection<TransactionEntry> ReceivedTransactions { get; set; } = new List<TransactionEntry>();
}
