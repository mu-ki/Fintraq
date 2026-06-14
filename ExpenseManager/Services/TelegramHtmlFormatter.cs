using System.Text;
using System.Text.RegularExpressions;

namespace ExpenseManager.Services;

/// <summary>Converts AI Markdown replies into Telegram HTML (parse_mode=HTML).</summary>
public static partial class TelegramHtmlFormatter
{
    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var normalized = markdown
            .Replace("\\*", "*", StringComparison.Ordinal)
            .Replace("\\_", "_", StringComparison.Ordinal)
            .Replace("\\#", "#", StringComparison.Ordinal)
            .Replace("\\-", "-", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var lines = normalized.Split('\n');
        var blocks = new List<string>();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                i++;
                continue;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var codeLines = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }

                if (i < lines.Length)
                {
                    i++;
                }

                blocks.Add($"<pre><code>{EscapeHtml(string.Join('\n', codeLines))}</code></pre>");
                continue;
            }

            if (trimmed.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
            {
                var body = new List<string>();
                i += 2;
                while (i < lines.Length && lines[i].Trim().Contains('|'))
                {
                    body.Add(lines[i]);
                    i++;
                }

                blocks.Add(RenderTable(line, body));
                continue;
            }

            if (TryMatchHeading(trimmed, out var heading))
            {
                blocks.Add($"<b>{FormatInline(heading)}</b>");
                i++;
                continue;
            }

            if (BulletRegex().IsMatch(trimmed))
            {
                var items = new List<string>();
                while (i < lines.Length && BulletRegex().IsMatch(lines[i].Trim()))
                {
                    items.Add(BulletRegex().Replace(lines[i].Trim(), string.Empty));
                    i++;
                }

                blocks.Add(string.Join('\n', items.Select(item => $"• {FormatInline(item)}")));
                continue;
            }

            var paragraph = new List<string>();
            while (i < lines.Length)
            {
                var t = lines[i].Trim();
                if (t.Length == 0 ||
                    t.Contains('|') ||
                    HeadingRegex().IsMatch(t) ||
                    BulletRegex().IsMatch(t) ||
                    t.StartsWith("```", StringComparison.Ordinal))
                {
                    break;
                }

                paragraph.Add(lines[i]);
                i++;
            }

            if (paragraph.Count > 0)
            {
                blocks.Add(FormatInline(string.Join(' ', paragraph)));
            }
        }

        return string.Join("\n\n", blocks);
    }

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = markdown
            .Replace("\\*", "*", StringComparison.Ordinal)
            .Replace("\\_", "_", StringComparison.Ordinal)
            .Replace("\\#", "#", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        text = BoldRegex().Replace(text, "$1");
        text = ItalicRegex().Replace(text, "$1");
        text = HeadingRegex().Replace(text, "$1");
        text = BulletRegex().Replace(text, "• ");
        text = TableSeparatorRegex().Replace(text, string.Empty);
        return text.Trim();
    }

    private static bool TryMatchHeading(string trimmed, out string heading)
    {
        var match = HeadingRegex().Match(trimmed);
        if (!match.Success)
        {
            heading = string.Empty;
            return false;
        }

        heading = match.Groups[1].Value;
        return true;
    }

    private static bool IsTableSeparator(string line) =>
        TableSeparatorRegex().IsMatch(line) && line.Contains('-');

    private static IReadOnlyList<string> ParseTableCells(string line)
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length > 0 && parts[0].Length == 0)
        {
            parts = parts[1..];
        }

        if (parts.Length > 0 && parts[^1].Length == 0)
        {
            parts = parts[..^1];
        }

        return parts;
    }

    private static string RenderTable(string headerLine, IReadOnlyList<string> bodyLines)
    {
        var headers = ParseTableCells(headerLine);
        if (headers.Count == 0)
        {
            return FormatInline(headerLine);
        }

        var rows = new List<string>();
        foreach (var bodyLine in bodyLines)
        {
            var cells = ParseTableCells(bodyLine);
            if (cells.Count == 0)
            {
                continue;
            }

            var row = new StringBuilder();
            row.Append("<b>").Append(EscapeHtml(cells[0])).Append("</b>");
            for (var c = 1; c < cells.Count && c < headers.Count; c++)
            {
                row.Append('\n').Append(EscapeHtml(headers[c])).Append(": ").Append(FormatInline(cells[c]));
            }

            rows.Add(row.ToString());
        }

        return string.Join("\n\n", rows);
    }

    private static string FormatInline(string text)
    {
        var result = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text.AsSpan(i).StartsWith("**"))
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Append(EscapeHtml(text[i..]));
                    break;
                }

                result.Append("<b>").Append(EscapeHtml(text[(i + 2)..end])).Append("</b>");
                i = end + 2;
                continue;
            }

            if (text.AsSpan(i).StartsWith("__"))
            {
                var end = text.IndexOf("__", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Append(EscapeHtml(text[i..]));
                    break;
                }

                result.Append("<b>").Append(EscapeHtml(text[(i + 2)..end])).Append("</b>");
                i = end + 2;
                continue;
            }

            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
            {
                var end = text.IndexOf('*', i + 1);
                if (end < 0 || (end + 1 < text.Length && text[end + 1] == '*'))
                {
                    result.Append(EscapeHtml(text[i].ToString()));
                    i++;
                    continue;
                }

                result.Append("<i>").Append(EscapeHtml(text[(i + 1)..end])).Append("</i>");
                i = end + 1;
                continue;
            }

            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end < 0)
                {
                    result.Append(EscapeHtml(text[i..]));
                    break;
                }

                result.Append("<code>").Append(EscapeHtml(text[(i + 1)..end])).Append("</code>");
                i = end + 1;
                continue;
            }

            var nextSpecial = FindNextSpecial(text, i);
            result.Append(EscapeHtml(text[i..nextSpecial]));
            i = nextSpecial;
        }

        return result.ToString();
    }

    private static int FindNextSpecial(string text, int start)
    {
        var indices = new[]
        {
            text.IndexOf("**", start, StringComparison.Ordinal),
            text.IndexOf("__", start, StringComparison.Ordinal),
            text.IndexOf('*', start),
            text.IndexOf('`', start)
        }.Where(index => index >= 0).DefaultIfEmpty(text.Length).Min();

        return indices;
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    [GeneratedRegex(@"^#{1,3}\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[-*•]\s+")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*\|?[\s:\-|]+\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicRegex();
}
