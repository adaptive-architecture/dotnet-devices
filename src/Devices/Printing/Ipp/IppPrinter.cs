namespace AdaptArch.Devices.Printing.Ipp;

/// <summary>
/// Prints to and queries a network printer over IPP (Internet Printing Protocol).
/// Tries IPPS (TLS) first and falls back to plain IPP across the well-known printer resources.
/// </summary>
/// <remarks>
/// The endpoint is resolved once and the resolved URI is kept for the life of the instance,
/// because every operation needs the same URI and a printer can take two round trips to probe.
/// The configuration is likewise read once and kept, so repeated calls to
/// <see cref="GetConfigurationAsync"/> cost nothing after the first.
/// </remarks>
public sealed class IppPrinter : IPrinter, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly IppEndpointResolver _resolver;
    private PrinterConfiguration? _configuration;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinter"/> class with an internally
    /// managed <see cref="HttpClient"/> and the default <see cref="IppTransportOptions"/>,
    /// which accept any server certificate, because network printers overwhelmingly use
    /// self-signed certificates.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    public IppPrinter(NetworkPrinterEndpoint endpoint)
        : this(endpoint, new IppTransportOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinter"/> class with an internally
    /// managed <see cref="HttpClient"/> built from <paramref name="options"/>.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="options">The certificate trust, the plain IPP fallback, and the connect timeout.</param>
    public IppPrinter(NetworkPrinterEndpoint endpoint, IppTransportOptions options)
        : this(endpoint, IppHttpClientFactory.Create(options), null, options, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinter"/> class with a
    /// caller-provided <see cref="HttpClient"/>. The client is not disposed by this instance.
    /// The library treats the client as one that validates certificates, so a failed TLS
    /// handshake throws instead of a fallback to plain IPP.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    public IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient)
        : this(endpoint, httpClient, null, IppTransportOptions.ForSuppliedClient(), false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    internal IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient, string? resourcePath, IppTransportOptions options)
        : this(endpoint, httpClient, resourcePath, options, false)
    {
    }

    private IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient, string? resourcePath, IppTransportOptions options, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);
        Endpoint = endpoint;
        Id = PrinterId.FromNetwork(endpoint.Host);
        Info = new PrinterInfo(Id, endpoint.Host);
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _resolver = new IppEndpointResolver(httpClient, endpoint.Host, endpoint.Port, resourcePath, options);
    }

    /// <inheritdoc />
    public PrinterId Id { get; }

    /// <inheritdoc />
    public PrinterEndpoint Endpoint { get; }

    /// <inheritdoc />
    public PrinterInfo Info { get; }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown when an option is not supported by the printer and <see cref="PrintOptions.OnUnsupported"/> is <see cref="UnsupportedOptionBehavior.Throw"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    /// <exception cref="TimeoutException">Thrown when the printer does not answer in time.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Thrown when the TLS handshake fails and this printer validates certificates.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public async Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveOptions = options;
        IReadOnlyList<string> dropped = [];
        if (options is not null && options.OnUnsupported != UnsupportedOptionBehavior.Send)
        {
            var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            effectiveOptions = PrintOptionValidator.Apply(options, configuration, out dropped);
        }

        return await _resolver.RunAsync(
            (uri, token) => IppRequests.SubmitAsync(_httpClient, uri, Id, payload, effectiveOptions, dropped, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _resolver.RunAsync((uri, token) => IppRequests.GetStatusAsync(_httpClient, uri, Id, token), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>The configuration is read once and the answer is kept for the life of this instance.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_configuration is not null)
        {
            return _configuration;
        }

        var configuration = await _resolver.RunAsync(
            (uri, token) => IppRequests.GetConfigurationAsync(_httpClient, uri, Id, token),
            cancellationToken).ConfigureAwait(false);
        _configuration = configuration;
        return configuration;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
