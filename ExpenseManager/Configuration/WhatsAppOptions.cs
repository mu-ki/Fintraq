namespace ExpenseManager.Configuration;

public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
}
