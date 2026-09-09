using System.Collections.Concurrent;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Routes job-queue operations to the transport that matches the printer identifier:
/// the operating system spooler for <see cref="PrinterIdKind.Spooler"/>, and IPP
/// (Internet Printing Protocol) for <see cref="PrinterIdKind.Network"/>.
/// </summary>
/// <remarks>
/// <see cref="PrinterIdKind.Usb"/> has no job queue to inspect, so every operation
/// throws <see cref="NotSupportedException"/> for it.
/// One <see cref="IppPrintJobQueue"/> is kept for the life of this instance for each
/// network host, because each holds its own endpoint resolver, and re-probing the
/// printer on every call would repeat a TLS handshake and up to four candidate URIs
/// before every job read. This instance is registered as a singleton and a polling
/// job monitor can watch several jobs at once, so the cache is a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> rather than a plain dictionary.
/// </remarks>
public sealed class CompositePrintJobQueue : IPrintJobQueue
{
    private readonly SpoolerPrintJobQueue _spoolerQueue;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, IppPrintJobQueue> _networkQueues = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePrintJobQueue"/> class.
    /// </summary>
    /// <param name="spoolerQueue">The queue used for <see cref="PrinterIdKind.Spooler"/> identifiers.</param>
    /// <param name="httpClient">The HTTP client used to build an <see cref="IppPrintJobQueue"/> for <see cref="PrinterIdKind.Network"/> identifiers.</param>
    public CompositePrintJobQueue(SpoolerPrintJobQueue spoolerQueue, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(spoolerQueue);
        ArgumentNullException.ThrowIfNull(httpClient);
        _spoolerQueue = spoolerQueue;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterIdKind.Usb"/> identifier.</exception>
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        Route(printerId).GetJobsAsync(printerId, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterIdKind.Usb"/> identifier.</exception>
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Route(printerId).GetJobAsync(printerId, jobId, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterIdKind.Usb"/> identifier.</exception>
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Route(printerId).CancelJobAsync(printerId, jobId, cancellationToken);

    private IPrintJobQueue Route(PrinterId printerId)
    {
        if (printerId.Kind == PrinterIdKind.Spooler)
        {
            return _spoolerQueue;
        }

        if (printerId.Kind == PrinterIdKind.Network)
        {
            return _networkQueues.GetOrAdd(
                printerId.Value,
                host => new IppPrintJobQueue(new NetworkPrinterEndpoint(host, IppPrinterStatusClient.DefaultPort), _httpClient));
        }

        throw new NotSupportedException($"Printer identifier kind '{printerId.Kind}' has no job queue.");
    }
}
