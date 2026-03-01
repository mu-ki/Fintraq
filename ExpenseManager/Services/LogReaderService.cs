namespace ExpenseManager.Services;

public sealed class LogReaderService : ILogReaderService
{
    public Task<IReadOnlyList<LogEntry>> GetRecentAsync(int count = 200, CancellationToken cancellationToken = default)
    {
        var list = InMemoryLogSink.GetRecent(count);
        return Task.FromResult(list);
    }
}
