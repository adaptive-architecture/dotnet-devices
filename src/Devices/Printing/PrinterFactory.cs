using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Default <see cref="IPrinterFactory"/>. Picks the printer by the scheme of the endpoint:
/// <see cref="IppPrinter"/> for <c>ipp</c> and <c>ipps</c>, <see cref="RawPrinter"/> for
/// <c>raw</c>, <see cref="SpoolerPrinter"/> for printers installed in the operating system
/// print spooler, and <see cref="CupsPrinter"/> for a queue of a CUPS server.
/// </summary>
/// <remarks>
/// Every <see cref="IppPrinter"/> and every <see cref="RawPrinter"/> this factory creates
/// shares one <see cref="HttpClient"/> and one <see cref="SnmpPrinterStatusClient"/>,
/// built from the <see cref="IppTransportOptions"/> of the factory. The default options
/// accept any server certificate, because network printers overwhelmingly use self-signed
/// certificates. A printer this factory returns owns no client, so a caller disposes the
/// factory, not the printers.
/// </remarks>
public sealed class PrinterFactory : IPrinterFactory, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IppTransportOptions _options;
    private readonly IppPrinterStatusClient _ippStatusClient;
    private readonly bool _ownsClient;
    private SnmpPrinterStatusClient? _snmpStatusClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with an
    /// internally managed <see cref="HttpClient"/> and the default <see cref="IppTransportOptions"/>.
    /// </summary>
    public PrinterFactory()
        : this(new IppTransportOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with an
    /// internally managed <see cref="HttpClient"/> built from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The certificate trust, the plain IPP fallback, and the connect timeout for every printer this factory creates.</param>
    public PrinterFactory(IppTransportOptions options)
        : this(IppHttpClientFactory.Create(options), options, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with a
    /// caller-provided <see cref="HttpClient"/>, shared by every printer this factory
    /// creates. The client is not disposed by this instance. The library treats the client
    /// as one that validates certificates, so a failed TLS handshake throws instead of a
    /// fallback to plain IPP.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for every printer this factory creates.</param>
    public PrinterFactory(HttpClient httpClient)
        : this(httpClient, IppTransportOptions.ForSuppliedClient(), false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="IppHttpClientFactory.Create"/>. The client
    /// is not disposed by this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for every printer this factory creates.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    public PrinterFactory(HttpClient httpClient, IppTransportOptions options)
        : this(httpClient, options, false)
    {
    }

    /// <summary>
    /// Gets the factory that makes the log of every printer this factory opens. Defaults to
    /// <c>null</c>, which falls back to <see cref="IppTransportOptions.LoggerFactory"/>, and
    /// then writes nothing. The log category is <c>AdaptArch.Devices.Printing</c>.
    /// </summary>
    /// <remarks>
    /// An application that uses <c>AddDevices()</c> or <c>AddPrinters()</c> needs no call
    /// here: the registration takes the <see cref="ILoggerFactory"/> of the container.
    /// Read <see href="https://adaptive-architecture.github.io/dotnet-devices/docs/troubleshooting.html">Troubleshooting</see>.
    /// </remarks>
    public ILoggerFactory? LoggerFactory { get; init; }

    /// <summary>
    /// Gets the formats every printer this factory opens knows, and the converters they
    /// may use. Defaults to <see cref="PrintFormatPolicy.Default"/>.
    /// </summary>
    public PrintFormatPolicy Formats { get; init; } = PrintFormatPolicy.Default;

    /// <summary>
    /// Gets the transport every <see cref="RawPrinter"/> this factory opens writes through.
    /// Defaults to <c>null</c>, which gives each raw printer a <see cref="TcpPrinterTransport"/>
    /// of its own with the default connect timeout and the log of this factory.
    /// </summary>
    public IPrinterTransport? Transport { get; init; }

    /// <summary>
    /// Gets the SNMP client every <see cref="RawPrinter"/> this factory opens reads its
    /// status and identity with. Defaults to <c>null</c>, which builds one with the default
    /// <see cref="SnmpPrinterStatusOptions"/> and the log of this factory.
    /// </summary>
    public SnmpPrinterStatusClient? SnmpStatusClient { get; init; }

    /// <summary>
    /// Gets the IPP client every <see cref="RawPrinter"/> this factory opens falls back to
    /// for its status. Defaults to <c>null</c>, which builds one on the <see cref="HttpClient"/>
    /// of this factory.
    /// </summary>
    public IppPrinterStatusClient? IppStatusClient { get; init; }

    private ILoggerFactory? EffectiveLoggerFactory => LoggerFactory ?? _options.LoggerFactory;

    // Built on first use: an init property is set after the constructor runs, so a client
    // built in the constructor would carry no log.
    private SnmpPrinterStatusClient EffectiveSnmpStatusClient =>
        SnmpStatusClient ?? LazyInitializer.EnsureInitialized(ref _snmpStatusClient, () => new SnmpPrinterStatusClient { LoggerFactory = EffectiveLoggerFactory });

    private PrinterFactory(HttpClient httpClient, IppTransportOptions options, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options;
        _ownsClient = ownsClient;
        _ippStatusClient = new IppPrinterStatusClient(httpClient, options);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for an endpoint type no transport handles.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public IPrinter Open(DiscoveredPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (printer.Endpoint is NetworkPrinterEndpoint network)
        {
            return network.Scheme is PrinterScheme.Ipp or PrinterScheme.Ipps
                ? new IppPrinter(network, _httpClient, null, _options) { Formats = Formats, LoggerFactory = EffectiveLoggerFactory }
                : OpenRaw(network);
        }

        if (printer.Endpoint is SpoolerPrinterEndpoint spooler)
        {
            return new SpoolerPrinter(spooler) { Formats = Formats, IppTransport = _options, LoggerFactory = EffectiveLoggerFactory };
        }

        if (printer.Endpoint is CupsPrinterEndpoint cups)
        {
            return new CupsPrinter(cups, _httpClient, _options, Formats) { LoggerFactory = EffectiveLoggerFactory };
        }

        throw new NotSupportedException($"Endpoint type '{printer.Endpoint.GetType().Name}' is not supported.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// The scheme of the identifier states the channel, so nothing is probed: the
    /// endpoint is built from the identifier and opened. An identifier that holds an
    /// identity instead of an address names no endpoint and has to be resolved through
    /// <see cref="IPrinterManager"/> first.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown for an identifier that holds a device identity.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (!id.TryCreateEndpoint(out var endpoint) || endpoint is null)
        {
            throw new NotSupportedException(
                $"'{id}' holds a device identity and not an address, so it names no endpoint. Resolve it through IPrinterManager first.");
        }

        return Task.FromResult(Open(new DiscoveredPrinter(id, endpoint, new PrinterInfo(id, id.Authority))));
    }

    private RawPrinter OpenRaw(NetworkPrinterEndpoint endpoint) =>
        new(endpoint, EffectiveSnmpStatusClient, IppStatusClient ?? _ippStatusClient) { Formats = Formats, Transport = Transport, LoggerFactory = EffectiveLoggerFactory };

    /// <summary>
    /// Disposes the internally managed <see cref="HttpClient"/>, if this instance owns one.
    /// A client supplied through <see cref="PrinterFactory(HttpClient)"/> is left open.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ippStatusClient.Dispose();
            if (_ownsClient)
            {
                _httpClient.Dispose();
            }
        }
    }
}
