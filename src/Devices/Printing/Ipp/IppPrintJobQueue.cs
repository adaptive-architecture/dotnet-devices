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
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    public IppPrintJobQueue(NetworkPrinterEndpoint endpoint, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _resolver = new IppEndpointResolver(httpClient, endpoint.Host, endpoint.Port, null);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await IppRequests.GetJobsAsync(_httpClient, uri, printerId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Returns <c>null</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await IppRequests.GetJobAsync(_httpClient, uri, printerId, jobId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Returns <c>false</c> when <paramref name="jobId"/> is not a number, or the printer reports the job as not found.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error other than "job not found".</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await IppRequests.CancelJobAsync(_httpClient, uri, jobId, cancellationToken).ConfigureAwait(false);
    }
}
