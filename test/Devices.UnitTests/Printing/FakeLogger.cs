using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.UnitTests.Printing;

// A logger factory that records what the library wrote. Hand-written, as every other fake
// in this suite is, so a test reads the entries without a mocking framework.
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    private readonly List<FakeLogEntry> _entries = [];
    private readonly List<string> _categories = [];
    private readonly AsyncLocal<IReadOnlyList<object>> _scopes = new();

    // Every logger this factory makes writes into one list, so a test reads the whole run in
    // order. Each entry names its category, so a test can read one area by itself.
    public IReadOnlyList<FakeLogEntry> Entries => _entries;

    public IReadOnlyList<string> Categories => _categories;

    public IReadOnlyList<int> EventIds => [.. _entries.ConvertAll(entry => entry.EventId)];

    /// <summary>
    /// The level below which nothing is written. Set it to prove that a guard works.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

    public FakeLogger Logger => new(this, "test");

    internal IReadOnlyList<object> CurrentScopes
    {
        get => _scopes.Value ?? [];
        set => _scopes.Value = value;
    }

    public IReadOnlyList<FakeLogEntry> Of(string category) =>
        _entries.FindAll(entry => String.Equals(entry.Category, category, StringComparison.Ordinal));

    public IReadOnlyList<FakeLogEntry> WithId(int eventId) => _entries.FindAll(entry => entry.EventId == eventId);

    internal void Add(FakeLogEntry entry) => _entries.Add(entry);

    public ILogger CreateLogger(string categoryName)
    {
        _categories.Add(categoryName);
        return new FakeLogger(this, categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeLogger : ILogger
{
    private readonly FakeLoggerFactory _factory;
    private readonly string _category;

    public FakeLogger(FakeLoggerFactory factory, string category)
    {
        _factory = factory;
        _category = category;
    }

    public IReadOnlyList<FakeLogEntry> Entries => _factory.Entries;

    public IReadOnlyList<int> EventIds => _factory.EventIds;

    // Scopes are kept per async flow, the way a real provider keeps them, so an entry
    // written inside one carries it even when the awaits hop threads.
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => new Scope(_factory, state);

    // The library guards each event itself, so this is what a test moves to prove a guard.
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _factory.MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
        _factory.Add(new FakeLogEntry(_category, logLevel, eventId.Id, formatter(state, exception), exception, _factory.CurrentScopes));

    private sealed class Scope : IDisposable
    {
        private readonly FakeLoggerFactory _factory;
        private readonly IReadOnlyList<object> _outer;

        public Scope(FakeLoggerFactory factory, object state)
        {
            _factory = factory;
            _outer = factory.CurrentScopes;
            factory.CurrentScopes = [.. _outer, state];
        }

        public void Dispose() => _factory.CurrentScopes = _outer;
    }
}

// Scopes are the states of the scopes open when the entry was written, outermost first.
internal sealed record FakeLogEntry(string Category, LogLevel Level, int EventId, string Message, Exception Exception, IReadOnlyList<object> Scopes);
