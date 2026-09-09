namespace AdaptArch.Devices.Printing;

/// <summary>
/// Single entry point that finds every reachable printer and prints to one of them
/// by its identifier.
/// </summary>
public interface IPrinterManager
{
    /// <summary>
    /// Runs every configured discovery source and combines the printers they found.
    /// </summary>
    /// <param name="options">The discovery scope. When <c>null</c>, defaults apply.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The printers found by the sources that succeeded.</returns>
    /// <exception cref="Exception">Thrown when every configured source failed. The exception is the last failure seen.</exception>
    Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(PrinterManagerOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Prints to a printer identified by <paramref name="id"/>.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="payload">The raw bytes and content type to print.</param>
    /// <param name="options">Optional per-job printing options.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The submitted job.</returns>
    Task<PrintJobInfo> PrintAsync(PrinterId id, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Watches a job on the printer identified by <paramref name="id"/>, yielding a
    /// reading each time its state or progress changes.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="options">The polling options.</param>
    /// <param name="cancellationToken">Token to stop watching.</param>
    /// <returns>A reading each time the job state or progress changes, ending with a terminal state or when the job leaves the queue.</returns>
    /// <exception cref="NotSupportedException">Thrown when the printer has no job queue to watch, for example a raw network channel.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="id"/> is unknown, even after a fresh discovery.</exception>
    IAsyncEnumerable<PrintJobInfo> WatchJobAsync(PrinterId id, string jobId, PrintJobMonitorOptions options, CancellationToken cancellationToken);
}
