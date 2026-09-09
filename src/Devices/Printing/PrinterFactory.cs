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
/// Every <see cref="IppPrinter"/> this factory creates shares one internally managed,
/// permissive <see cref="HttpClient"/> that accepts any server certificate, because
/// network printers overwhelmingly use self-signed certificates. A printer this
/// factory returns therefore owns nothing and does not need disposing.
/// </remarks>
public sealed class PrinterFactory : IPrinterFactory, IDisposable
{
    private const string UsbNotSupportedMessage = "USB printers are not supported yet.";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with an
    /// internally managed, permissive <see cref="HttpClient"/> shared by every
    /// <see cref="IppPrinter"/> this factory creates.
    /// </summary>
    public PrinterFactory()
        : this(IppPrinter.CreateDefaultClient(), true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterFactory"/> class with a
    /// caller-provided <see cref="HttpClient"/>, shared by every <see cref="IppPrinter"/>
    /// this factory creates. The client is not disposed by this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for every <see cref="IppPrinter"/> this factory creates.</param>
    public PrinterFactory(HttpClient httpClient)
        : this(httpClient, false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    private PrinterFactory(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Thrown for a <see cref="UsbPrinterEndpoint"/>.</exception>
    public IPrinter Open(DiscoveredPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(printer);

        if (printer.Endpoint is NetworkPrinterEndpoint network)
        {
            if (network.Port == IppPrinterStatusClient.DefaultPort)
            {
                return new IppPrinter(network, _httpClient);
            }

            return new RawPrinter(network);
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
    public async Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)
    {
        if (id.Kind == PrinterIdKind.Usb)
        {
            throw new NotSupportedException(UsbNotSupportedMessage);
        }

        if (id.Kind == PrinterIdKind.Spooler)
        {
            return new SpoolerPrinter(new SpoolerPrinterEndpoint(id.Value));
        }

        NetworkPrinterEndpoint ippEndpoint = new(id.Value, IppPrinterStatusClient.DefaultPort);
        var ippPrinter = new IppPrinter(ippEndpoint, _httpClient);
        try
        {
            _ = await ippPrinter.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            return ippPrinter;
        }
        catch (InvalidOperationException)
        {
            NetworkPrinterEndpoint rawEndpoint = new(id.Value, NetworkPrinterEndpoint.DefaultPort);
            return new RawPrinter(rawEndpoint);
        }
    }

    /// <summary>
    /// Disposes the internally managed <see cref="HttpClient"/>, if this instance owns one.
    /// A client supplied through <see cref="PrinterFactory(HttpClient)"/> is left open.
    /// </summary>
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
