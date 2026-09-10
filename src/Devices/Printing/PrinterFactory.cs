using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Default <see cref="IPrinterFactory"/>. Picks <see cref="IppPrinter"/> for network
/// printers that answer on the IPP port, <see cref="RawPrinter"/> for every other
/// network port, and <see cref="SpoolerPrinter"/> for printers installed in the
/// operating system print spooler.
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
    private readonly SnmpPrinterStatusClient _snmpStatusClient = new();
    private readonly bool _ownsClient;
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
                ? new IppPrinter(network, _httpClient, null, _options)
                : OpenRaw(network);
        }

        if (printer.Endpoint is SpoolerPrinterEndpoint spooler)
        {
            return new SpoolerPrinter(spooler);
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

    private RawPrinter OpenRaw(NetworkPrinterEndpoint endpoint) => new(endpoint, _snmpStatusClient, _ippStatusClient);

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
