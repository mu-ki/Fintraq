namespace ExpenseManager.Services;

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
}
