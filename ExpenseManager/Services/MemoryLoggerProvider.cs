using Microsoft.Extensions.Logging;

namespace ExpenseManager.Services;

public sealed class MemoryLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new MemoryLogger(categoryName);
    public void Dispose() { }
}

public sealed class MemoryLogger : ILogger
{
    private readonly string _category;

    public MemoryLogger(string category) => _category = category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var exStr = exception?.ToString();
        InMemoryLogSink.Append(new LogEntry
        {
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            Level = logLevel.ToString(),
            Category = _category,
            Message = message,
            Exception = exStr
        });
    }
}
