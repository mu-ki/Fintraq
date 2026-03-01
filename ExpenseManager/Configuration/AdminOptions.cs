namespace ExpenseManager.Configuration;

public sealed class AdminOptions
{
    public const string SectionName = "Admin";
    public string AdminEmail { get; set; } = string.Empty;
}
