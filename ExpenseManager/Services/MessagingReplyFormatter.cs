using ExpenseManager.Models.Messaging;

namespace ExpenseManager.Services;

public static class MessagingReplyFormatter
{
    private const int TelegramMaxLength = 4096;
    private const int WhatsAppMaxLength = 4096;

    public sealed record FormattedReply(string Text, string? ParseMode, string PlainFallback);

    public static FormattedReply Format(MessagingChannel channel, string text)
    {
        var normalized = text.Trim();

        if (channel == MessagingChannel.Telegram)
        {
            var html = TelegramHtmlFormatter.ToHtml(normalized);
            var plain = TelegramHtmlFormatter.ToPlainText(normalized);
            return new FormattedReply(
                Truncate(html, TelegramMaxLength),
                "HTML",
                Truncate(plain, TelegramMaxLength));
        }

        return new FormattedReply(
            Truncate(normalized, WhatsAppMaxLength),
            null,
            Truncate(normalized, WhatsAppMaxLength));
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var truncated = text[..(maxLength - 60)].TrimEnd();
        return $"{truncated}\n\n(View full details on the Fintraq website.)";
    }
}
