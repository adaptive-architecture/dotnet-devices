using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Inspects and manages the job queue of a printer installed in the operating system
/// print spooler (Win32 print queues, CUPS destinations).
/// </summary>
public sealed class SpoolerPrintJobQueue : IPrintJobQueue
{
    private ISpoolerDriver? _driver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpoolerPrintJobQueue"/> class, using
    /// the spooler driver for the current operating system.
    /// </summary>
    public SpoolerPrintJobQueue()
    {
    }

    internal SpoolerPrintJobQueue(ISpoolerDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
    }

    /// <summary>
    /// Gets the IPP policy of this printer: the log, the raw-response switch and the
    /// certificate trust. Defaults to <c>null</c>, which is the default policy.
    /// </summary>
    /// <remarks>
    /// It reaches the CUPS spooler, which speaks IPP. The Windows spooler is native interop
    /// and reads none of it.
    /// </remarks>
    public IppTransportOptions? IppTransport { get; init; }

    /// <summary>
    /// Gets the factory that makes the log. Defaults to <c>null</c>, which falls back to
    /// the factory of <see cref="IppTransport"/>, and then writes nothing.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    // The type holds an IppTransportOptions, so its factory is the fallback: a caller that
    // set only the transport policy still gets the log.
    private ILoggerFactory? EffectiveLoggerFactory => LoggerFactory ?? IppTransport?.LoggerFactory;

    // Built on first use, because an init property is set after the constructor runs.
    // LazyInitializer, not "??=": this type is registered as a singleton, and two concurrent
    // first calls must not each build a driver.
    private ISpoolerDriver Driver =>
        LazyInitializer.EnsureInitialized(ref _driver, () => SpoolerDriverFactory.Create(null, IppTransport, EffectiveLoggerFactory));

    /// <inheritdoc />
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        Driver.GetJobsAsync(QueueName(printerId), cancellationToken);

    /// <inheritdoc />
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Driver.GetJobAsync(QueueName(printerId), jobId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Driver.CancelJobAsync(QueueName(printerId), jobId, cancellationToken);

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
