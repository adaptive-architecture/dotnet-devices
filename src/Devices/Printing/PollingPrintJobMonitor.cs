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
    public async IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId printerId,
        string jobId,
        PrintJobMonitorOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(options);

        // A timeout ends the watch quietly. Only the caller's token throws. The delay
        // still has to watch both tokens, or the deadline would not be noticed until the
        // running delay ends on its own, so the wait uses a linked token; the "which one
        // fired" check right after the wait is what keeps the two outcomes apart.
        using var deadline = options.Timeout is TimeSpan limit
            ? new CancellationTokenSource(limit, _timeProvider)
            : new CancellationTokenSource();
        using var delayToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        PrintJobState? lastState = null;
        int? lastCount = null;
        while (!deadline.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = await _queue.GetJobAsync(printerId, jobId, cancellationToken).ConfigureAwait(false);
            if (reading is null)
            {
                // Both CUPS and the Windows spooler drop a finished job from the queue, so
                // a job that is gone has finished.
                var done = new PrintJobInfo(jobId, printerId, PrintJobState.Completed)
                {
                    CompletedAt = _timeProvider.GetUtcNow(),
                };
                yield return done;
                yield break;
            }

            if (reading.State != lastState || reading.ImpressionsCompleted != lastCount)
            {
                lastState = reading.State;
                lastCount = reading.ImpressionsCompleted;
                yield return reading;
            }

            if (IsTerminal(reading.State))
            {
                yield break;
            }

            try
            {
                await Task.Delay(options.PollInterval, _timeProvider, delayToken.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The deadline fired, not the caller. The while condition below ends the
                // loop quietly, with no exception, on the next check.
            }
        }
    }

    private static bool IsTerminal(PrintJobState state) =>
        state == PrintJobState.Completed || state == PrintJobState.Failed || state == PrintJobState.Canceled;
}
