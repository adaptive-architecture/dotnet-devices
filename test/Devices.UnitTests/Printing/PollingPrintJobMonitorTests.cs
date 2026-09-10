using System.Diagnostics;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PollingPrintJobMonitorTests
{
    private static readonly PrinterId Printer = PrinterId.FromNetwork("printer.local");
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
            PrinterId.FromSpooler("lobby"), "1", new PrintJobMonitorOptions { PollInterval = TimeSpan.Zero }, TestContext.Current.CancellationToken));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => monitor.WatchJobAsync(
            PrinterId.FromSpooler("lobby"), "1", new PrintJobMonitorOptions { Timeout = TimeSpan.FromSeconds(-1) }, TestContext.Current.CancellationToken));
    }
}
