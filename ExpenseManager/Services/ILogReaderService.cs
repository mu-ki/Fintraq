namespace ExpenseManager.Services;

public interface ILogReaderService
{
    Task<IReadOnlyList<LogEntry>> GetRecentAsync(int count = 200, CancellationToken cancellationToken = default);
    Task WipeAsync(CancellationToken cancellationToken = default);
}
