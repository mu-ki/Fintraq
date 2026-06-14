using ExpenseManager.Models.Ai;

namespace ExpenseManager.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.Gemini;
}
