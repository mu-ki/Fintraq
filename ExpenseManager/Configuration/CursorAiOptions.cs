namespace ExpenseManager.Configuration;

public sealed class CursorAiOptions
{
    public const string SectionName = "Cursor";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "composer-2";
}
