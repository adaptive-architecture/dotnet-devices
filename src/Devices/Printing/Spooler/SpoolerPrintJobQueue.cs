namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Inspects and manages the job queue of a printer installed in the operating system
/// print spooler (Win32 print queues, CUPS destinations).
/// </summary>
public sealed class SpoolerPrintJobQueue : IPrintJobQueue
{
    private readonly ISpoolerDriver _driver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrintJobQueue"/> class, using
    /// the spooler driver for the current operating system.
    /// </summary>
    public SpoolerPrintJobQueue()
        : this(SpoolerDriverFactory.Create())
    {
    }

    internal SpoolerPrintJobQueue(ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        _driver.GetJobsAsync(printerId.Value, cancellationToken);

    /// <inheritdoc />
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.GetJobAsync(printerId.Value, jobId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.CancelJobAsync(printerId.Value, jobId, cancellationToken);
}
