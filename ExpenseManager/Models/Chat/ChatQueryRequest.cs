using System.ComponentModel.DataAnnotations;

namespace ExpenseManager.Models.Chat;

public sealed class ChatQueryRequest
{
    [Required]
    [MaxLength(4000)]
    public string Message { get; set; } = string.Empty;
}
