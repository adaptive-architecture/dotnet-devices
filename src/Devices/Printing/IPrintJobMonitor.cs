namespace AdaptArch.Devices.Printing;

/// <summary>
/// Watches a print job and reports each change until the job reaches a terminal state.
/// </summary>
public interface IPrintJobMonitor
{
    /// <summary>
    /// Watches a print job, yielding a reading each time its state or progress changes.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="options">The polling options.</param>
    /// <param name="cancellationToken">Token to stop watching. Cancelling this token throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>A reading each time the job state or progress changes, ending with a terminal state or when the job leaves the queue.</returns>
    IAsyncEnumerable<PrintJobInfo> WatchJobAsync(PrinterId printerId, string jobId, PrintJobMonitorOptions options, CancellationToken cancellationToken);
}
