using System.ComponentModel.DataAnnotations;

namespace ExpenseManager.Models;

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public CategoryType Type { get; set; }

    public bool IsSystem { get; set; } = true;

    public ICollection<TransactionEntry> Transactions { get; set; } = new List<TransactionEntry>();
}
