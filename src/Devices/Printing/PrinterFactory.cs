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
    private const string UsbNotSupportedMessage = "USB printers are not supported yet.";

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
    /// <exception cref="NotSupportedException">Thrown for a <see cref="UsbPrinterEndpoint"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public IPrinter Open(DiscoveredPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (printer.Endpoint is NetworkPrinterEndpoint network)
        {
            if (network.Port == IppPrinterStatusClient.DefaultPort)
            {
                return new IppPrinter(network, _httpClient, null, _options);
            }

            return OpenRaw(network);
        }

        if (printer.Endpoint is SpoolerPrinterEndpoint spooler)
        {
            return new SpoolerPrinter(spooler);
        }

        if (printer.Endpoint is UsbPrinterEndpoint)
        {
            throw new NotSupportedException(UsbNotSupportedMessage);
        }

        throw new NotSupportedException($"Endpoint type '{printer.Endpoint.GetType().Name}' is not supported.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// A network identifier first tries IPP on the well-known IPP port; when the printer
    /// answers no IPP request at all, it falls back to the raw port 9100 channel.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown for a <see cref="PrinterIdKind.Usb"/> identifier.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Thrown when the TLS handshake fails and this factory validates certificates.</exception>
    public async Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (id.Kind == PrinterIdKind.Usb)
        {
            throw new NotSupportedException(UsbNotSupportedMessage);
        }

        if (id.Kind == PrinterIdKind.Spooler)
        {
            return new SpoolerPrinter(new SpoolerPrinterEndpoint(id.Value));
        }

        NetworkPrinterEndpoint ippEndpoint = new(id.Value, IppPrinterStatusClient.DefaultPort);
        var ippPrinter = new IppPrinter(ippEndpoint, _httpClient, null, _options);
        try
        {
            _ = await ippPrinter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return ippPrinter;
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            // No IPP answer, or a service on port 631 that is not IPP.
            return OpenRaw(new NetworkPrinterEndpoint(id.Value, NetworkPrinterEndpoint.DefaultPort));
        }
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
