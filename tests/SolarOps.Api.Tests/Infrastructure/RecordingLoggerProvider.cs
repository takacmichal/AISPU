using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SolarOps.Api.Tests.Infrastructure;

/// <summary>Collects log entries written by the test host, so tests can assert that failures were logged.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

    public IReadOnlyCollection<RecordedLogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<RecordedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new RecordedLogEntry(category, logLevel, formatter(state, exception), exception));
    }
}

public sealed record RecordedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);
