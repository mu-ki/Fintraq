using System.ComponentModel.DataAnnotations;
using ExpenseManager.Models;

namespace ExpenseManager.Models.Chat;

public sealed class ChatMessage : AuditableEntity
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = string.Empty;

    [Required]
    [MaxLength(8000)]
    public string Content { get; set; } = string.Empty;
}
