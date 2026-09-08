namespace AdaptArch.Devices.Printing;

/// <summary>
/// Inspects and manages the job queues of printers.
/// </summary>
public interface IPrintJobQueue
{
    /// <summary>
    /// Lists the jobs currently known for a printer.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The queued and recent jobs.</returns>
    Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a single job by identifier.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The job, or <c>null</c> when it is unknown.</returns>
    Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a queued or printing job.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><c>true</c> when the job was canceled; <c>false</c> when it was unknown or already terminal.</returns>
    Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken);
}
