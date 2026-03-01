namespace ExpenseManager.Models;

public class AiTokenUsage
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public DateTime CalledAt { get; set; }
}
