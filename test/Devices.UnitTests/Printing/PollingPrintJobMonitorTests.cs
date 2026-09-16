using System.Diagnostics;
using System.Linq;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PollingPrintJobMonitorTests
{
    private static readonly PrinterId Printer = PrinterId.ForRaw("printer.local");
    private static readonly PrintJobMonitorOptions Fast = new() { PollInterval = TimeSpan.FromMilliseconds(1) };

    [Fact]
    public async Task WatchJobAsync_YieldsEachChangeAndStopsAtATerminalState()
    {
        FakeQueue queue = new(
            Job(PrintJobState.Queued, null),
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Printing, 2),
            Job(PrintJobState.Completed, 2));
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(4, seen.Count);
        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_DoesNotRepeatAnUnchangedReading()
    {
        FakeQueue queue = new(
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Printing, 1),
            Job(PrintJobState.Completed, 1));
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task WatchJobAsync_ReportsCompletedWhenTheJobLeavesTheQueue()
    {
        // Both CUPS and the Windows spooler drop a finished job, so an absent job is done.
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), null);
        PollingPrintJobMonitor monitor = new(queue);

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_StopsWhenTheCallerCancels()
    {
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), Job(PrintJobState.Printing, 2));
        PollingPrintJobMonitor monitor = new(queue);
        using CancellationTokenSource source = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var job in monitor.WatchJobAsync(Printer, "42", Fast, source.Token))
            {
                await source.CancelAsync();
            }
        });
    }

    [Fact]
    public async Task WatchJobAsync_EndsQuietlyWhenTheTimeoutExpires()
    {
        // The job never changes and never becomes terminal, so only the timeout ends the watch.
        FakeQueue queue = new InfiniteQueue(Job(PrintJobState.Printing, 1));
        PollingPrintJobMonitor monitor = new(queue);
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(1), Timeout = TimeSpan.FromMilliseconds(20) };

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        var seenJob = Assert.Single(seen);
        Assert.Equal(PrintJobState.Printing, seenJob.State);
    }

    [Fact]
    public async Task WatchJobAsync_EndsAtTheTimeoutEvenWhenThePollIntervalIsLonger()
    {
        // Regression: the timeout must cut a running poll delay short. The long poll
        // interval is what makes the bug visible.
        FakeQueue queue = new InfiniteQueue(Job(PrintJobState.Printing, 1));
        PollingPrintJobMonitor monitor = new(queue);
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(500), Timeout = TimeSpan.FromMilliseconds(20) };

        var stopwatch = Stopwatch.StartNew();
        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }
        stopwatch.Stop();

        Assert.Single(seen);
        Assert.True(stopwatch.ElapsedMilliseconds < 250, $"Expected the watch to end near the 20ms timeout, not the 500ms poll interval; it took {stopwatch.ElapsedMilliseconds}ms.");
    }

    private static PrintJobInfo Job(PrintJobState state, int? done) =>
        new("42", Printer, state) { ImpressionsCompleted = done };

    private class FakeQueue : IPrintJobQueue
    {
        private readonly Queue<PrintJobInfo> _readings;

        public FakeQueue(params PrintJobInfo[] readings) => _readings = new Queue<PrintJobInfo>(readings);

        public virtual Task<PrintJobInfo> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
            Task.FromResult(_readings.Count > 0 ? _readings.Dequeue() : null);

        public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InfiniteQueue : FakeQueue
    {
        private readonly PrintJobInfo _reading;

        public InfiniteQueue(PrintJobInfo reading) => _reading = reading;

        public override Task<PrintJobInfo> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
            Task.FromResult<PrintJobInfo>(_reading);
    }

    [Fact]
    public void WatchJobAsync_RejectsANonPositivePollIntervalOrTimeoutAtTheCall()
    {
        PollingPrintJobMonitor monitor = new(new FakeQueue());

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => monitor.WatchJobAsync(
            PrinterId.ForSpooler("lobby"), "1", new PrintJobMonitorOptions { PollInterval = TimeSpan.Zero }, TestContext.Current.CancellationToken));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => monitor.WatchJobAsync(
            PrinterId.ForSpooler("lobby"), "1", new PrintJobMonitorOptions { Timeout = TimeSpan.FromSeconds(-1) }, TestContext.Current.CancellationToken));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => monitor.WatchJobAsync(
            PrinterId.ForSpooler("lobby"), "1", new PrintJobMonitorOptions { IdleTimeout = TimeSpan.Zero }, TestContext.Current.CancellationToken));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => monitor.WatchJobAsync(
            PrinterId.ForSpooler("lobby"), "1", new PrintJobMonitorOptions { IdleTimeout = TimeSpan.FromSeconds(-1) }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WatchJobAsync_EndsQuietlyWhenNothingChangesForTheIdleTimeout()
    {
        // The job never changes and never becomes terminal, so only the idle timeout ends
        // the watch. The clock steps a second per reading, so it is reached in a fixed
        // number of polls and the test waits for none of it.
        FakeQueue queue = new InfiniteQueue(Job(PrintJobState.Printing, 1));
        PollingPrintJobMonitor monitor = new(queue, new SteppingClock(TimeSpan.FromSeconds(1)));
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(1), IdleTimeout = TimeSpan.FromSeconds(5) };

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        var seenJob = Assert.Single(seen);
        Assert.Equal(PrintJobState.Printing, seenJob.State);
    }

    [Fact]
    public async Task WatchJobAsync_KeepsWatchingWhileThePageCountRises()
    {
        // The defect this option exists for: a printer that woke from sleep printed slowly
        // but steadily, and a wall-clock cap killed a job that was working. Every reading
        // here moves the page count on, and the total time runs far past the idle timeout,
        // so the watch must see all of them.
        List<PrintJobInfo> readings = [.. Enumerable.Range(1, 10).Select(page => Job(PrintJobState.Printing, page))];
        readings.Add(Job(PrintJobState.Completed, 10));
        FakeQueue queue = new([.. readings]);
        PollingPrintJobMonitor monitor = new(queue, new SteppingClock(TimeSpan.FromSeconds(1)));
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(1), IdleTimeout = TimeSpan.FromSeconds(5) };

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(11, seen.Count);
        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_TreatsAStateChangeAsProgress()
    {
        // The page count stays unknown throughout, so only the state says the job moved.
        FakeQueue queue = new(
            Job(PrintJobState.Queued, null),
            Job(PrintJobState.Queued, null),
            Job(PrintJobState.Printing, null),
            Job(PrintJobState.Completed, null));
        PollingPrintJobMonitor monitor = new(queue, new SteppingClock(TimeSpan.FromSeconds(1)));
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(1), IdleTimeout = TimeSpan.FromSeconds(4) };

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        Assert.Equal(3, seen.Count);
        Assert.Equal(PrintJobState.Completed, seen[^1].State);
    }

    [Fact]
    public async Task WatchJobAsync_EndsAtWhicheverOfTheTwoLimitsComesFirst()
    {
        // The idle timeout is unreachable, so the absolute timeout is what ends this watch.
        FakeQueue queue = new InfiniteQueue(Job(PrintJobState.Printing, 1));
        PollingPrintJobMonitor monitor = new(queue);
        PrintJobMonitorOptions options = new()
        {
            PollInterval = TimeSpan.FromMilliseconds(1),
            Timeout = TimeSpan.FromMilliseconds(20),
            IdleTimeout = TimeSpan.FromHours(1),
        };

        List<PrintJobInfo> seen = [];
        await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, TestContext.Current.CancellationToken))
        {
            seen.Add(job);
        }

        _ = Assert.Single(seen);
    }

    [Fact]
    public async Task WatchJobAsync_StillThrowsWhenTheCallerCancels()
    {
        // The distinction that caused the defect: a limit ends the watch quietly, and only
        // the caller's own token throws. A caller must never pass its deadline as a token.
        FakeQueue queue = new InfiniteQueue(Job(PrintJobState.Printing, 1));
        PollingPrintJobMonitor monitor = new(queue);
        PrintJobMonitorOptions options = new() { PollInterval = TimeSpan.FromMilliseconds(1), IdleTimeout = TimeSpan.FromHours(1) };
        using CancellationTokenSource source = new();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var job in monitor.WatchJobAsync(Printer, "42", options, source.Token))
            {
                await source.CancelAsync();
            }
        });
    }

    // A clock that steps forward on every reading, so a watch reaches an idle timeout in a
    // fixed number of polls instead of in real time. Only the reading of "now" is faked:
    // timers stay on the system clock, so the poll delay is the real one millisecond and
    // an absolute Timeout still behaves as it does in production.
    private sealed class SteppingClock : TimeProvider
    {
        private readonly TimeSpan _step;
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public SteppingClock(TimeSpan step) => _step = step;

        public override DateTimeOffset GetUtcNow()
        {
            _now += _step;
            return _now;
        }
    }

    [Fact]
    public async Task WatchJobAsync_AJobThatPrintedAndThenVanishedIsInformation()
    {
        // The normal end on CUPS: the queue drops a job that finished.
        FakeLoggerFactory factory = new();
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), null);
        PollingPrintJobMonitor monitor = new(queue) { LoggerFactory = factory };

        await foreach (var _ in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(LogLevel.Information, Assert.Single(factory.WithId(2061)).Level);
        Assert.Empty(factory.WithId(2062));
    }

    [Fact]
    public async Task WatchJobAsync_AJobThatVanishedBeforeItPrintedIsAWarning()
    {
        // A cancel and a purge arrive as the same signal, and the watch reports Completed
        // for all three, so the entry is the only thing that keeps them apart.
        FakeLoggerFactory factory = new();
        FakeQueue queue = new(Job(PrintJobState.Queued, null), null);
        PollingPrintJobMonitor monitor = new(queue) { LoggerFactory = factory };

        await foreach (var _ in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
        }

        var warning = Assert.Single(factory.WithId(2062));
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("42", warning.Message, StringComparison.Ordinal);
        Assert.Empty(factory.WithId(2061));
    }

    [Fact]
    public async Task WatchJobAsync_SaysWhyTheWatchEnded()
    {
        FakeLoggerFactory factory = new();
        FakeQueue queue = new(Job(PrintJobState.Completed, 1));
        PollingPrintJobMonitor monitor = new(queue) { LoggerFactory = factory };

        await foreach (var _ in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
        }

        Assert.Contains("terminal state", Assert.Single(factory.WithId(2063)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WatchJobAsync_TheTraceReadingsStopAtTheLevelThatIsOn()
    {
        // Proves the guard the generator writes: nothing below the level costs a message.
        FakeLoggerFactory factory = new() { MinimumLevel = LogLevel.Information };
        FakeQueue queue = new(Job(PrintJobState.Printing, 1), Job(PrintJobState.Completed, 1));
        PollingPrintJobMonitor monitor = new(queue) { LoggerFactory = factory };

        await foreach (var _ in monitor.WatchJobAsync(Printer, "42", Fast, TestContext.Current.CancellationToken))
        {
        }

        Assert.Empty(factory.WithId(2064));
        Assert.NotEmpty(factory.WithId(2060));
    }
}
