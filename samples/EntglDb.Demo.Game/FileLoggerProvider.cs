using Microsoft.Extensions.Logging;

namespace EntglDb.Demo.Game;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly Lock _lock = new();
    private readonly LogLevel _minLevel;

    public FileLoggerProvider(string filePath, LogLevel minLevel = LogLevel.Trace)
    {
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _writer, _lock, _minLevel);

    public void Dispose() => _writer.Dispose();
}

file sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly StreamWriter _writer;
    private readonly Lock _lock;
    private readonly LogLevel _minLevel;

    public FileLogger(string category, StreamWriter writer, Lock @lock, LogLevel minLevel)
    {
        _category = category;
        _writer = writer;
        _lock = @lock;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel,-11}] {_category}: {formatter(state, exception)}";
        if (exception != null)
            line += $"\n{exception}";

        lock (_lock)
            _writer.WriteLine(line);
    }
}
