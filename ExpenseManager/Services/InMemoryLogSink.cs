using System.Collections.Concurrent;

namespace ExpenseManager.Services;

/// <summary>
/// In-memory ring buffer of recent log entries for admin log viewer.
/// </summary>
public static class InMemoryLogSink
{
    private const int MaxEntries = 2000;
    private static readonly ConcurrentQueue<LogEntry> Buffer = new();

    public static void Append(LogEntry entry)
    {
        Buffer.Enqueue(entry);
        while (Buffer.Count > MaxEntries && Buffer.TryDequeue(out _)) { }
    }

    public static IReadOnlyList<LogEntry> GetRecent(int count = 200)
    {
        var list = Buffer.ToArray();
        if (list.Length <= count)
            return list.Reverse().ToArray();
        return list.TakeLast(count).Reverse().ToArray();
    }

    public static void Clear()
    {
        while (Buffer.TryDequeue(out _)) { }
    }
}
