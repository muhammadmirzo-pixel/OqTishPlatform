using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OqTish.IntegrationTests.Infrastructure;

/// <summary>Captures every formatted log message, structured value and scope (for the S16 redaction test).</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public string AllText => string.Join('\n', _entries);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            entries.Enqueue($"[scope] {category}: {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> kv
                ? string.Join(';', kv.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {values} | {exception}");
        }
    }
}
