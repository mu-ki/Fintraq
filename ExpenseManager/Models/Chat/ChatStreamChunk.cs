namespace ExpenseManager.Models.Chat;

public sealed class ChatStreamChunk
{
    public string Type { get; set; } = string.Empty;
    public string? Content { get; set; }
    public DateTime? Timestamp { get; set; }
    public bool? RequiresClarification { get; set; }
}
