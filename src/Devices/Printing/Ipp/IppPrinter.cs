
using System.Diagnostics.CodeAnalysis;

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
    /// managed <see cref="HttpClient"/> that accepts any server certificate, because network
    /// printers overwhelmingly use self-signed certificates.
    /// Supply your own client for custom certificate validation.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    public IppPrinter(NetworkPrinterEndpoint endpoint)
        : this(endpoint, CreateDefaultClient(), null, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinter"/> class with a
    /// caller-provided <see cref="HttpClient"/>. The client is not disposed by this instance.
    /// </summary>
    /// <param name="endpoint">The network endpoint of the printer.</param>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    public IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient)
        : this(endpoint, httpClient, null, false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    internal IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient, string? resourcePath)
        : this(endpoint, httpClient, resourcePath, false)
    {
    }

    private IppPrinter(NetworkPrinterEndpoint endpoint, HttpClient httpClient, string? resourcePath, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
        Id = PrinterId.FromNetwork(endpoint.Host);
        Info = new PrinterInfo(Id, endpoint.Host);
        _httpClient = httpClient;
        _ownsClient = ownsClient;
        _resolver = new IppEndpointResolver(httpClient, endpoint.Host, endpoint.Port, resourcePath);
    }

    // Shared with PrinterFactory, so every IppPrinter the factory hands out accepts the
    // same self-signed printer certificates the factory itself already accepts for
    // status reads, instead of a second, default-validating client that can reach the
    // printer over plain IPP only.
    [SuppressMessage(
        "Critical Vulnerability",
        "S4830:Server certificates should be verified during SSL/TLS connections",
        Justification = "Network printers overwhelmingly use self-signed certificates, so a validating client reaches almost none of them over IPPS. A caller that needs validation supplies its own HttpClient; see docs/printers.md.")]
    internal static HttpClient CreateDefaultClient()
    {
        SocketsHttpHandler handler = new();
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return new HttpClient(handler, true);
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
    public async Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);

        var effectiveOptions = options;
        IReadOnlyList<string> dropped = [];
        if (options is not null && options.OnUnsupported != UnsupportedOptionBehavior.Send)
        {
            var configuration = await GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            effectiveOptions = PrintOptionValidator.Apply(options, configuration, out dropped);
        }

        return await IppRequests.SubmitAsync(_httpClient, uri, Id, payload, effectiveOptions, dropped, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await IppRequests.GetStatusAsync(_httpClient, uri, Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>The configuration is read once and the answer is kept for the life of this instance.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var configuration = await IppRequests.GetConfigurationAsync(_httpClient, uri, Id, cancellationToken).ConfigureAwait(false);
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
