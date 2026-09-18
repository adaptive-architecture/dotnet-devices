namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// Lists the print queues of one CUPS server, over the network.
/// </summary>
/// <remarks>
/// This is the only discovery in the library that finds queues rather than devices, and
/// the only one that is told where to look instead of searching: a CUPS server is not
/// announced on the local link, and asking it is only useful when the application has said
/// which server to ask. Every queue it reports is a <see cref="PrinterScheme.Cups"/>
/// channel; the device behind each one still reaches
/// <see cref="DiscoveredPrinter.Aliases"/>, so a queue found here and the same printer
/// found over multicast DNS still group into one <see cref="PrinterDevice"/>.
/// </remarks>
public sealed class CupsPrinterDiscovery : IPrinterDiscovery
{
    private readonly CupsSpoolerDriver _driver;

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrinterDiscovery"/> class.
    /// </summary>
    /// <param name="host">The host name or IP address of the CUPS server.</param>
    /// <param name="httpClient">
    /// The HTTP client used to send IPP requests. Not disposed by this instance. The library
    /// treats the client as one that validates certificates, so a failed TLS handshake
    /// throws instead of a fallback to plain IPP.
    /// </param>
    public CupsPrinterDiscovery(string host, HttpClient httpClient)
        : this(host, IppPrinterStatusClient.DefaultPort, httpClient, IppTransportOptions.ForSuppliedClient())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CupsPrinterDiscovery"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="Ipp.IppHttpClientFactory.Create"/>.
    /// </summary>
    /// <param name="host">The host name or IP address of the CUPS server.</param>
    /// <param name="port">The TCP port of the CUPS server.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests. Not disposed by this instance.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the credentials, the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    /// <exception cref="ArgumentException">Thrown when the host is not a host.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1 to 65535.</exception>
    public CupsPrinterDiscovery(string host, int port, HttpClient httpClient, IppTransportOptions options)
    {
        NetworkPrinterEndpoint.ThrowIfNotAHost(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        Host = host;
        Port = port;
        _driver = CupsSpoolerDriver.ForServer(host, port, httpClient, options, null);
    }

    /// <summary>
    /// Gets the host name or IP address of the CUPS server this discovery asks.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the TCP port of the CUPS server this discovery asks.
    /// </summary>
    public int Port { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken) =>
        _driver.EnumeratePrintersAsync(cancellationToken);
}
