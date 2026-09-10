namespace AdaptArch.Devices.Printing.Ipp;

/// <summary>
/// Inspects and manages the job queue of a network printer over IPP (Internet Printing Protocol).
/// </summary>
/// <remarks>
/// The endpoint is resolved once and the resolved URI is kept for the life of the instance,
/// because every operation needs the same URI and a printer can take two round trips to probe.
/// A polling job monitor calls this queue repeatedly, so a fresh resolver per call would
/// double every poll's round trips.
/// </remarks>
public sealed class IppPrintJobQueue : IPrintJobQueue
{
    private readonly HttpClient _httpClient;
    private readonly IppEndpointResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrintJobQueue"/> class.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="httpClient">
    /// The HTTP client used to send IPP requests. Not disposed by this instance. The library
    /// treats the client as one that validates certificates, so a failed TLS handshake
    /// throws instead of a fallback to plain IPP.
    /// </param>
    public IppPrintJobQueue(NetworkPrinterEndpoint endpoint, HttpClient httpClient)
        : this(endpoint, httpClient, IppTransportOptions.ForSuppliedClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrintJobQueue"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="IppHttpClientFactory.Create"/>.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    public IppPrintJobQueue(NetworkPrinterEndpoint endpoint, HttpClient httpClient, IppTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _resolver = new IppEndpointResolver(httpClient, endpoint.Host, endpoint.Port, null, options);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        _resolver.RunAsync((uri, token) => IppRequests.GetJobsAsync(_httpClient, uri, printerId, token), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Returns <c>null</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _resolver.RunAsync((uri, token) => IppRequests.GetJobAsync(_httpClient, uri, printerId, jobId, token), cancellationToken);

    /// <inheritdoc />
    /// <remarks>Returns <c>false</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _resolver.RunAsync((uri, token) => IppRequests.CancelJobAsync(_httpClient, uri, jobId, token), cancellationToken);
}
