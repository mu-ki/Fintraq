using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public static class MessagingReplyFormatter
{
    private const int TelegramMaxLength = 4096;
    private const int WhatsAppMaxLength = 4096;

    public static string Format(MessagingChannel channel, string text)
    {
        var normalized = text.Trim();
        var maxLength = channel == MessagingChannel.Telegram ? TelegramMaxLength : WhatsAppMaxLength;

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var truncated = normalized[..(maxLength - 60)].TrimEnd();
        return $"{truncated}\n\n(View full details on the Fintraq website.)";
    }
}
