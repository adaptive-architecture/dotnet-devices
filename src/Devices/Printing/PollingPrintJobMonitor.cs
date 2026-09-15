using System.Runtime.CompilerServices;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Watches a print job by reading the job queue again and again. This works with every
/// printer, because it needs no notification channel.
/// </summary>
public sealed class PollingPrintJobMonitor : IPrintJobMonitor
{
    private readonly IPrintJobQueue _queue;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingPrintJobMonitor"/> class.
    /// </summary>
    /// <param name="queue">The queue to read.</param>
    public PollingPrintJobMonitor(IPrintJobQueue queue)
        : this(queue, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PollingPrintJobMonitor"/> class with
    /// a caller-provided clock. Tests supply a clock so that no test waits.
    /// </summary>
    /// <param name="queue">The queue to read.</param>
    /// <param name="timeProvider">The clock used between reads.</param>
    public PollingPrintJobMonitor(IPrintJobQueue queue, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _queue = queue;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <see cref="PrintJobMonitorOptions.PollInterval"/>, <see cref="PrintJobMonitorOptions.Timeout"/> or <see cref="PrintJobMonitorOptions.IdleTimeout"/> is zero or negative.</exception>
    public IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId printerId,
        string jobId,
        PrintJobMonitorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PollInterval, TimeSpan.Zero);
        if (options.Timeout is TimeSpan timeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        }

        if (options.IdleTimeout is TimeSpan idle)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idle, TimeSpan.Zero);
        }

        return WatchJobAsyncCore(printerId, jobId, options, cancellationToken);
    }

    // The guards live in the public method, so they throw before the first enumeration.
    private async IAsyncEnumerable<PrintJobInfo> WatchJobAsyncCore(
        PrinterId printerId,
        string jobId,
        PrintJobMonitorOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Either timeout ends the watch quietly; only the caller's token throws. The delay
        // waits on both, and the check after it keeps the two outcomes apart.
        using var deadline = options.Timeout is TimeSpan limit
            ? new CancellationTokenSource(limit, _timeProvider)
            : new CancellationTokenSource();
        using var delayToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        PrintJobState? lastState = null;
        int? lastCount = null;

        // When the job last moved. A job that is slow is not a job that is stuck, so it is
        // the absence of change that ends the watch, never the time the job has taken.
        var lastChange = _timeProvider.GetUtcNow();
        while (!deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = await _queue.GetJobAsync(printerId, jobId, cancellationToken).ConfigureAwait(false);
            if (reading is null)
            {
                // Both spoolers drop a finished job, so a job that is gone has finished.
                var now = _timeProvider.GetUtcNow();
                var done = new PrintJobInfo(jobId, printerId, PrintJobState.Completed)
                {
                    CreatedAt = now,
                    CompletedAt = now,
                };
                yield return done;
                yield break;
            }

            if (reading.State != lastState || reading.ImpressionsCompleted != lastCount)
            {
                lastState = reading.State;
                lastCount = reading.ImpressionsCompleted;
                lastChange = _timeProvider.GetUtcNow();
                yield return reading;
            }

            if (IsTerminal(reading.State))
            {
                yield break;
            }

            // Checked after the read rather than on a timer of its own: the watch can only
            // notice a change when it reads, so the poll interval is the granularity either
            // way, and one clock reading is cheaper than another token to wait on.
            if (options.IdleTimeout is TimeSpan quiet && _timeProvider.GetUtcNow() - lastChange >= quiet)
            {
                yield break;
            }

            try
            {
                await Task.Delay(options.PollInterval, _timeProvider, delayToken.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The deadline fired, not the caller: the loop condition ends the watch.
            }
        }
    }

    private static bool IsTerminal(PrintJobState state) =>
        state == PrintJobState.Completed || state == PrintJobState.Failed || state == PrintJobState.Canceled;
}
