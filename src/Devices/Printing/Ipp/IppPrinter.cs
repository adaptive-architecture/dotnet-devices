using Microsoft.Extensions.Logging;

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
public sealed class IppPrinter : IPrinter, IQueueEvidenceChannel, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly IppContext _context;
    private readonly IppEndpointResolver _resolver;
    private PrinterConfiguration? _configuration;
    private ILogger? _logger;
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
        Id = PrinterId.FromEndpoint(endpoint);
        Info = new PrinterInfo(Id, endpoint.Host);
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _context = new IppContext(httpClient, options);
        _resolver = new IppEndpointResolver(_context, endpoint.Host, endpoint.Port, resourcePath);
    }

    /// <inheritdoc />
    public PrinterId Id { get; }

    /// <inheritdoc />
    public PrinterEndpoint Endpoint { get; }

    /// <inheritdoc />
    public PrinterInfo Info { get; }

    /// <summary>
    /// Gets the transport and the endpoint that answered, or <c>null</c> before the first
    /// call resolved one. A transport failure clears it, so the next call probes again.
    /// </summary>
    /// <remarks>
    /// This printer tries IPPS (TLS) first and plain IPP next, so a downgrade to clear text
    /// is otherwise invisible. Read this after a call to see which one answered.
    /// </remarks>
    public PrinterConnection? Connection => PrinterConnections.From(_resolver.Resolved);

    /// <summary>
    /// Gets the formats this printer knows. Defaults to <see cref="PrintFormatPolicy.Default"/>.
    /// </summary>
    /// <remarks>
    /// A printer language is sent unchanged, so which content types count as one decides
    /// what this printer negotiates with an IPP server.
    /// </remarks>
    public PrintFormatPolicy Formats { get; init; } = PrintFormatPolicy.Default;

    /// <summary>
    /// Gets the capture that receives each document as it is submitted, converted where the
    /// library converted it. Defaults to <c>null</c>, which captures nothing.
    /// </summary>
    public IPrintCapture? Capture
    {
        get => _context.Capture;
        init => _context.Capture = value;
    }

    /// <summary>
    /// Gets the factory that makes the log of the options a job lost. Defaults to
    /// <c>null</c>, which falls back to <see cref="IppTransportOptions.LoggerFactory"/>, and
    /// then writes nothing.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    private ILogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => PrintingLog.Create(LoggerFactory ?? _context.Options.LoggerFactory));

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
        using var scope = IppLogScope.Begin(_context.Logger, Id, "Print");

        // The pages and the resolution are judged after the conversion has decided who
        // applies them: a converter selects pages a printer cannot, and renders at a
        // resolution from the raster list, not from the one the printer prints at itself.
        var effectiveOptions = options;
        IReadOnlyList<DroppedOption> dropped = [];
        PrinterConfiguration? judged = null;
        if (options is not null && options.OnUnsupported != UnsupportedOptionBehavior.Send)
        {
            judged = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
            effectiveOptions = PrintOptionValidator.Apply(options, judged, ConverterOptions.Exclude, out dropped);
        }

        // A printer language must never be re-typed by the server. Only a raw payload
        // needs the printer's format list, so every other job costs no extra request.
        var format = payload.ContentType;
        if (IppDocumentFormat.IsRawLanguage(format, Formats))
        {
            var configuration = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
            format = IppDocumentFormat.Negotiate(format, configuration.SupportedDocumentFormats, Formats);

            var endpoint = _resolver.Resolved ?? await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
            IppLog.DocumentFormatChosen(_context.Logger, payload.ContentType, endpoint, format);

            // The printer named no format it knows, so the job goes as opaque bytes. A
            // server that re-types them prints the command source of the label instead.
            if (String.Equals(format, PrinterContentTypes.OctetStream, StringComparison.OrdinalIgnoreCase))
            {
                IppLog.DocumentFormatDowngraded(_context.Logger, endpoint, payload.ContentType, format);
            }
        }

        var submission = await IppDocumentConversion.ConvertIfNeededAsync(ConversionChannel, payload, format, effectiveOptions, cancellationToken).ConfigureAwait(false);
        var sendOptions = submission.Options;
        if (judged is not null && !submission.IsConverted)
        {
            // Nothing rendered, so the printer applies the pages and the resolution itself.
            sendOptions = PrintOptionValidator.Apply(submission.Options, judged, ConverterOptions.Only, out var late);
            dropped = [.. dropped, .. late];
        }

        var job = await _resolver.RunAsync(
            (uri, token) => IppRequests.SubmitAsync(
                _context,
                uri,
                Id,
                new IppSubmission(submission.Payload, submission.Format, sendOptions, dropped) { ConverterUsed = submission.ConverterUsed },
                token),
            cancellationToken).ConfigureAwait(false);
        PrintOptionValidator.Prepend(job, submission.Dropped);
        PrintingLog.ReportDropped(Logger, job);
        return job;
    }

    private IppConversionChannel ConversionChannel => new(
        Formats,
        Id,
        _context.Logger,
        ReadConfigurationAsync,
        async token => _resolver.Resolved ?? await _resolver.ResolveAsync(token).ConfigureAwait(false),
        IppDocumentFormat.IppTargets);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scope = IppLogScope.Begin(_context.Logger, Id, "GetStatus");
        return await _resolver.RunAsync((uri, token) => IppRequests.GetStatusAsync(_context, uri, Id, token), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>The configuration is read once and the answer is kept for the life of this instance.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scope = IppLogScope.Begin(_context.Logger, Id, "GetConfiguration");
        return await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
    }

    // The read behind GetConfigurationAsync, for the operations that need it inside their
    // own scope.
    private async Task<PrinterConfiguration> ReadConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        var configuration = await _resolver.RunAsync(
            (uri, token) => IppRequests.GetConfigurationAsync(_context, uri, Id, token),
            cancellationToken).ConfigureAwait(false);
        _configuration = configuration;
        return configuration;
    }

    /// <inheritdoc />
    /// <remarks>Reads <c>printer-uuid</c> and <c>printer-device-id</c>, both of which are optional.</remarks>
    public async Task<PrinterIdentity?> GetIdentityAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var scope = IppLogScope.Begin(_context.Logger, Id, "GetIdentity");
        var identity = await _resolver.RunAsync(
            (uri, token) => IppRequests.GetIdentityAsync(_context, uri, token),
            cancellationToken).ConfigureAwait(false);
        return identity.IsEmpty ? null : identity;
    }

    // The queue evidence the printer manager correlates with. Implemented explicitly: it
    // is a detail of grouping and not part of what a caller does with a printer.
    Task<IReadOnlyList<PrinterQueueFingerprint>> IQueueEvidenceChannel.ReadQueueAsync(string requestingUserName, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _resolver.RunAsync(
            (uri, token) => IppRequests.GetQueueFingerprintsAsync(_context, uri, requestingUserName, token),
            cancellationToken);
    }

    Task<QueueTracerSupport> IQueueEvidenceChannel.ReadTracerSupportAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _resolver.RunAsync(
            (uri, token) => IppRequests.GetTracerSupportAsync(_context, uri, token),
            cancellationToken);
    }

    Task<string?> IQueueEvidenceChannel.CreateTracerJobAsync(string jobName, string requestingUserName, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _resolver.RunAsync(
            (uri, token) => IppRequests.CreateTracerJobAsync(_context, uri, jobName, requestingUserName, token),
            cancellationToken);
    }

    async Task IQueueEvidenceChannel.CancelTracerJobAsync(string jobId, string requestingUserName, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = await _resolver.RunAsync(
            (uri, token) => IppRequests.CancelJobAsync(_context, uri, jobId, requestingUserName, token),
            cancellationToken).ConfigureAwait(false);
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
