#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.CupsHostTests;

// Keeps the event of every entry, which is all these tests read.
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<int> _eventIds = new();

    public IReadOnlyList<int> EventIds => _eventIds.ToList();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(_eventIds);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<int> _eventIds;

        public RecordingLogger(ConcurrentQueue<int> eventIds) => _eventIds = eventIds;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _eventIds.Enqueue(eventId.Id);
    }
}
