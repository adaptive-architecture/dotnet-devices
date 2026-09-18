namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Inspects and manages the job queues of one CUPS server, over the network.
/// </summary>
/// <remarks>
/// One instance serves every queue of the server, because the queue name comes from the
/// identifier of each call and the address is the same for all of them.
/// </remarks>
public sealed class CupsPrintJobQueue : IPrintJobQueue
{
    private readonly CupsSpoolerDriver _driver;
    private readonly string _host;

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrintJobQueue"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the CUPS server.</param>
    /// <param name="httpClient">
    /// The HTTP client used to send IPP requests. Not disposed by this instance. The library
    /// treats the client as one that validates certificates, so a failed TLS handshake
    /// throws instead of a fallback to plain IPP.
    /// </param>
    public CupsPrintJobQueue(string host, HttpClient httpClient)
        : this(host, IppPrinterStatusClient.DefaultPort, httpClient, IppTransportOptions.ForSuppliedClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrintJobQueue"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="Ipp.IppHttpClientFactory.Create"/>.
    /// </summary>
    /// <param name="host">The host name or IP address of the CUPS server.</param>
    /// <param name="port">The TCP port of the CUPS server.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the credentials, the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    /// <exception cref="ArgumentException">Thrown when the host is not a host.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1 to 65535.</exception>
    public CupsPrintJobQueue(string host, int port, HttpClient httpClient, IppTransportOptions options)
    {
        NetworkPrinterEndpoint.ThrowIfNotAHost(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _host = host;
        _driver = CupsSpoolerDriver.ForServer(host, port, httpClient, options, null);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for an identifier that names no queue on this server.</exception>
    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(PrinterId printerId, CancellationToken cancellationToken) =>
        _driver.GetJobsAsync(QueueName(printerId), cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for an identifier that names no queue on this server.</exception>
    public Task<PrintJobInfo?> GetJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.GetJobAsync(QueueName(printerId), jobId, cancellationToken);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for an identifier that names no queue on this server.</exception>
    public Task<bool> CancelJobAsync(PrinterId printerId, string jobId, CancellationToken cancellationToken) =>
        _driver.CancelJobAsync(QueueName(printerId), jobId, cancellationToken);

    // The host is checked and not only the scheme: this instance speaks to one server, and
    // asking it about a queue of another would answer about whatever queue of its own
    // happens to share the name.
    private string QueueName(PrinterId printerId)
    {
        if (printerId.Scheme != PrinterScheme.Cups || !printerId.TryGetQueueName(out var name))
        {
            throw new NotSupportedException(
                $"'{printerId}' does not name a queue of a CUPS server. Resolve it through IPrinterManager first.");
        }

        if (!printerId.TryGetHost(out var host) || !String.Equals(host, _host, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"'{printerId}' is not a queue of '{_host}'.");
        }

        return name;
    }
}
