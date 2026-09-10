using System.Collections.Concurrent;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Routes job-queue operations to the transport the scheme of the identifier names: the
/// operating system spooler for <see cref="PrinterScheme.Spooler"/>, and IPP (Internet
/// Printing Protocol) for <see cref="PrinterScheme.Ipp"/> and <see cref="PrinterScheme.Ipps"/>.
/// </summary>
/// <remarks>
/// <see cref="PrinterScheme.Raw"/> has no job queue to inspect, so every operation throws
/// <see cref="NotSupportedException"/> for it.
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
    private readonly IppTransportOptions _options;
    private readonly ConcurrentDictionary<string, IppPrintJobQueue> _networkQueues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePrintJobQueue"/> class.
    /// </summary>
    /// <param name="spoolerQueue">The queue used for <see cref="PrinterScheme.Spooler"/> identifiers.</param>
    /// <param name="httpClient">
    /// The HTTP client used to build an <see cref="IppPrintJobQueue"/> for <see cref="PrinterScheme.Ipp"/>
    /// identifiers. The library treats the client as one that validates certificates, so a
    /// failed TLS handshake throws instead of a fallback to plain IPP.
    /// </param>
    public CompositePrintJobQueue(SpoolerPrintJobQueue spoolerQueue, HttpClient httpClient)
        : this(spoolerQueue, httpClient, IppTransportOptions.ForSuppliedClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositePrintJobQueue"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="IppHttpClientFactory.Create"/>.
    /// </summary>
    /// <param name="spoolerQueue">The queue used for <see cref="PrinterScheme.Spooler"/> identifiers.</param>
    /// <param name="httpClient">The HTTP client used to build an <see cref="IppPrintJobQueue"/> for <see cref="PrinterScheme.Ipp"/> identifiers.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    public CompositePrintJobQueue(SpoolerPrintJobQueue spoolerQueue, HttpClient httpClient, IppTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(spoolerQueue);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _spoolerQueue = spoolerQueue;
        _httpClient = httpClient;
        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterScheme.Raw"/> identifier, and for one that holds a device identity.</exception>
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        Route(printerId).GetJobsAsync(printerId, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterScheme.Raw"/> identifier, and for one that holds a device identity.</exception>
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Route(printerId).GetJobAsync(printerId, jobId, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterScheme.Raw"/> identifier, and for one that holds a device identity.</exception>
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        Route(printerId).CancelJobAsync(printerId, jobId, cancellationToken);

    private IPrintJobQueue Route(PrinterId printerId)
    {
        if (printerId.Scheme == PrinterScheme.Spooler)
        {
            return _spoolerQueue;
        }

        if (printerId.Scheme is PrinterScheme.Ipp or PrinterScheme.Ipps)
        {
            if (!printerId.TryGetHost(out var host))
            {
                throw new NotSupportedException(
                    $"'{printerId}' names an identity and not a host, so no queue can be opened for it. Resolve it through IPrinterManager first.");
            }

            // The whole identifier is the key, not the host: the IPP and the IPPS channel
            // of one host are two queues and must not share a resolver.
            return _networkQueues.GetOrAdd(
                printerId.ToString(),
                _ => new IppPrintJobQueue(new NetworkPrinterEndpoint(host, printerId.Scheme, printerId.Port), _httpClient, _options));
        }

        if (printerId.Scheme == PrinterScheme.Raw)
        {
            // A raw channel gives back no job identifier, so there is no job to read. The
            // old code fell through to IPP here and asked port 631 about a job it never
            // saw, which reported every job as finished the moment it was sent.
            throw new NotSupportedException(
                $"'{printerId}' is a raw channel, which has no job queue.");
        }

        throw new NotSupportedException($"Printer scheme '{PrinterSchemes.Format(printerId.Scheme)}' has no job queue.");
    }
}
