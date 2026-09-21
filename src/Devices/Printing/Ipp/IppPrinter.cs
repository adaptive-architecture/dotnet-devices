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

        // A printer language must never be re-typed by the server. Only a raw payload
        // needs the printer's format list, so every other job costs no extra request.
        var format = payload.ContentType;
        if (IppDocumentFormat.IsRawLanguage(format, Formats))
        {
            var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
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

        var submission = await ConvertIfNeededAsync(payload, format, effectiveOptions, cancellationToken).ConfigureAwait(false);

        return await _resolver.RunAsync(
            (uri, token) => IppRequests.SubmitAsync(_context, uri, Id, new IppSubmission(submission.Payload, submission.Format, submission.Options, dropped), token),
            cancellationToken).ConfigureAwait(false);
    }

    // A raster is only readable at a resolution the printer rasters at, so the request is
    // moved to the nearest one it named rather than sent as asked and refused. A printer
    // that named none takes what the job asked for.
    private static int ResolveDpi(int? requested, IReadOnlyList<int> supported)
    {
        var dpi = requested ?? PrintConversionContext.DefaultDpi;
        return supported.Count == 0 || supported.Contains(dpi)
            ? dpi
            : supported.MinBy(candidate => Math.Abs(candidate - dpi));
    }

    // Geometry the printer cannot be asked for, so the page has to be rendered for it.
    private static bool NeedsRendering(PrintOptions? options) =>
        options is not null
        && (options.MediaSizeSource == MediaSizeSource.Document || options.Placement?.IsEmpty == false);

    // How large the sheet is, for a converter that composes the page onto it. The dimensions
    // a job carries win over the name, because a name is only as good as the size it encodes;
    // a legacy keyword such as "letter" encodes none, and the converter is then told nothing
    // rather than told a guess.
    private static MediaDimensions? ResolveMedia(PrintOptions? options, string? mediaName)
    {
        if (options?.MediaDimensions is MediaDimensions dimensions)
        {
            return dimensions;
        }

        return PwgMediaNames.TryParse(mediaName, out var parsed) ? parsed : null;
    }

    // The part of the sheet the page is fitted into. A job that asked for the physical page,
    // a printer that reported no margins, and a margin set that would leave nothing to print
    // on all answer null, which is the whole sheet.
    internal static ImageRectangle? ResolveFitArea(PrintOptions? options, MediaDimensions? media, MediaMargins? margins, int dpi)
    {
        if (media is null || margins is null || margins.IsEmpty)
        {
            return null;
        }

        if (options?.FitArea == PrintFitArea.Physical)
        {
            return null;
        }

        var left = margins.Left.ToPixels(dpi);
        var top = margins.Top.ToPixels(dpi);
        var width = media.Width.ToPixels(dpi) - left - margins.Right.ToPixels(dpi);
        var height = media.Height.ToPixels(dpi) - top - margins.Bottom.ToPixels(dpi);
        return width > 0 && height > 0 ? new ImageRectangle(left, top, width, height) : null;
    }

    // Grayscale for a job that asked for it and a printer that offers it, and colour
    // otherwise. A printer that named no type leaves the choice to the converter.
    private static string? ResolveRasterType(IReadOnlyList<string> types, PrintColorMode? colorMode)
    {
        if (types.Count == 0)
        {
            return null;
        }

        if (colorMode == PrintColorMode.Monochrome)
        {
            var gray = types.FirstOrDefault(static type => type.StartsWith("sgray", StringComparison.OrdinalIgnoreCase));
            if (gray is not null)
            {
                return gray;
            }
        }

        return types.FirstOrDefault(static type => type.StartsWith("srgb", StringComparison.OrdinalIgnoreCase)) ?? types[0];
    }

    // A document the printer cannot read is rendered to a format it can, when a converter
    // is registered for it. Everything else passes through: a printer that lists the format
    // reads the document itself, which is always better than a raster of it -- unless the job
    // named the converter, which is the one way of saying otherwise.
    private async Task<(PrinterPayload Payload, string Format, PrintOptions? Options)> ConvertIfNeededAsync(
        PrinterPayload payload,
        string format,
        PrintOptions? options,
        CancellationToken cancellationToken)
    {
        if (Formats.KindOf(payload.ContentType) != PrinterFormatKind.Document)
        {
            return (payload, format, options);
        }

        // The converter is looked for before the format list is read, because an application
        // that registered none converts nothing whatever the printer answers, and this path
        // must not cost it a request it never needed.
        var converter = Formats.ConverterFor(payload.ContentType, options?.ConverterName);
        if (converter is null)
        {
            // A job that named a converter asked for that one, so rendering with another or
            // sending the document unchanged would both be the wrong answer to a question
            // the caller did ask.
            PrintConverters.ThrowIfNamed(Formats, payload.ContentType, options?.ConverterName);
            return (payload, format, options);
        }

        var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var supported = configuration.SupportedDocumentFormats;

        // The document itself is the better thing to send when nobody said otherwise: the
        // printer's own interpreter beats any raster of ours and the job is a fraction of the
        // size. Naming a converter is saying otherwise. Nobody sets that as a preference, so
        // a job that carries one is asking for that engine to run, and a printer that happens
        // to read the format too must not quietly decide it should not.
        // A placement and a document media size are geometry nobody but a renderer can apply:
        // there is no IPP attribute for either, so a job that asks for one is converted even
        // where the printer reads the document, exactly as a job that named an engine is.
        if (options?.ConverterName is null
            && !NeedsRendering(options)
            && supported.Contains(payload.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return (payload, format, options);
        }

        var target = IppDocumentFormat.NegotiateConversionTarget(supported, converter);
        if (target is null)
        {
            // Passing through is still the right answer where the printer reads the document,
            // but a job that named a converter should not have to infer from a printed page
            // that the name went nowhere.
            var unreachable = _resolver.Resolved ?? await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
            IppLog.DocumentNotConverted(
                _context.Logger,
                payload.ContentType,
                unreachable,
                options?.ConverterName is null
                    ? "the printer reads no format the converter writes"
                    : $"the printer reads no format converter '{options.ConverterName}' writes");
            return (payload, format, options);
        }

        var endpoint = _resolver.Resolved ?? await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);

        var dpi = ResolveDpi(options?.ResolutionDpi, configuration.PwgRasterResolutionsDpi);
        var mediaName = options?.MediaSize ?? configuration.DefaultMediaSize;
        var media = ResolveMedia(options, mediaName);

        PrintConversionContext context = new(
            payload.ContentType,
            target,
            dpi,
            options?.PageRanges,
            Id.ToString())
        {
            RasterType = ResolveRasterType(configuration.PwgRasterTypes, options?.ColorMode),
            SheetBack = configuration.PwgRasterSheetBack,
            Duplex = options?.Duplex,
            MediaName = mediaName,
            MediaWidthPixels = media?.Width.ToPixels(dpi),
            MediaHeightPixels = media?.Height.ToPixels(dpi),
            FitArea = ResolveFitArea(options, media, configuration.DefaultMediaMargins, dpi),
            Scaling = options?.Scaling,
            Orientation = options?.Orientation,
            Placement = options?.Placement,
            Smoothing = options?.Smoothing,
            MediaSizeSource = options?.MediaSizeSource ?? MediaSizeSource.Printer,
            DocumentPassword = options?.DocumentPassword,
        };

        var documents = await converter.ConvertAsync(payload.Data.ToArray(), context, cancellationToken).ConfigureAwait(false);

        // Every target this negotiates carries each page in one stream, so one document is
        // the only valid answer. A converter that returned one page each would otherwise
        // have all but the first silently dropped.
        if (documents is not { Count: 1 })
        {
            throw new InvalidOperationException(
                $"The converter of '{payload.ContentType}' returned {documents?.Count ?? 0} documents for '{target}', " +
                $"which carries every page in one. Printer '{Id}' was sent nothing.");
        }

        IppLog.DocumentConverted(_context.Logger, payload.ContentType, endpoint, target);
        IppLog.DocumentConversionSize(_context.Logger, payload.ContentType, endpoint, documents[0].Length, target);

        // The converter selected the pages, so the printer must not select them again -- and
        // where it also placed the page on its media, the printer must not fit it again.
        var converted = options is null
            ? null
            : converter.PlacesOnMedia(context)
                ? PrintOptionValidator.WithoutPlacedGeometry(options)
                : PrintOptionValidator.WithoutPageRanges(options);
        return (PrinterPayload.FromBytes(documents[0], target), target, converted);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _resolver.RunAsync((uri, token) => IppRequests.GetStatusAsync(_context, uri, Id, token), cancellationToken);
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
