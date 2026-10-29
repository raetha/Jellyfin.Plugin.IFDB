using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Ifdb.Tests.Support;

/// <summary>
/// Captures log messages so tests can check what was logged.
/// </summary>
internal sealed class ListLogger : ILogger
{
    private readonly LogLevel _minimum;

    public ListLogger(LogLevel minimum = LogLevel.Trace)
    {
        _minimum = minimum;
    }

    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public bool Has(LogLevel level, string text)
        => Entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.Ordinal));

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimum;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Enqueue((logLevel, formatter(state, exception), exception));
}
