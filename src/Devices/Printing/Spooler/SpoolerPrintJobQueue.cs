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
        _driver.GetJobsAsync(QueueName(printerId), cancellationToken);

    /// <inheritdoc />
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.GetJobAsync(QueueName(printerId), jobId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.CancelJobAsync(QueueName(printerId), jobId, cancellationToken);

    // A queue is addressed by its name, so an identifier that holds an identity instead
    // has to be resolved through IPrinterManager before it reaches a driver.
    private static string QueueName(PrinterId printerId)
    {
        if (!printerId.TryGetQueueName(out var name))
        {
            throw new NotSupportedException(
                $"'{printerId}' does not name a print queue. Resolve it through IPrinterManager first.");
        }

        return name;
    }
}
