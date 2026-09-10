using System.Collections.Concurrent;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reads identity and status details from network printers over IPP (Internet Printing Protocol).
/// Tries IPPS (TLS) first and falls back to plain IPP across the well-known printer resources.
/// All operations are read-only; nothing is submitted for printing.
/// </summary>
/// <remarks>
/// The IPP wire format is handled by <c>SharpIppNext</c>. Supply levels are read from the
/// raw attributes of the response, because the typed model of that library does not carry
/// the <c>marker-*</c> attributes.
/// </remarks>
public sealed class IppPrinterStatusClient : IDisposable
{
    /// <summary>
    /// Default IPP port assigned by IANA.
    /// </summary>
    public const int DefaultPort = 631;

    private readonly HttpClient _httpClient;
    private readonly IppTransportOptions _options;
    private readonly bool _ownsClient;
    // One resolver per printer, so a repeated status read costs one round trip, not a probe
    // plus a read. The key ignores the case of the host name.
    private readonly ConcurrentDictionary<string, IppEndpointResolver> _resolvers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with an
    /// internally managed <see cref="HttpClient"/> and the default <see cref="IppTransportOptions"/>,
    /// which accept any server certificate, because network printers overwhelmingly use
    /// self-signed certificates.
    /// </summary>
    public IppPrinterStatusClient()
        : this(new IppTransportOptions())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with an
    /// internally managed <see cref="HttpClient"/> built from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The certificate trust, the plain IPP fallback, and the connect timeout.</param>
    public IppPrinterStatusClient(IppTransportOptions options)
        : this(IppHttpClientFactory.Create(options), options, true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with a
    /// caller-provided <see cref="HttpClient"/>. The client is not disposed by this instance.
    /// The library treats the client as one that validates certificates, so a failed TLS
    /// handshake throws instead of a fallback to plain IPP.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    public IppPrinterStatusClient(HttpClient httpClient)
        : this(httpClient, IppTransportOptions.ForSuppliedClient(), false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with a
    /// caller-provided <see cref="HttpClient"/> and the <see cref="IppTransportOptions"/> it
    /// was built from, for example by <see cref="IppHttpClientFactory.Create"/>. The client
    /// is not disposed by this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    /// <param name="options">The policy of <paramref name="httpClient"/>: the certificate trust and the plain IPP fallback. Pass the options the client was built from.</param>
    public IppPrinterStatusClient(HttpClient httpClient, IppTransportOptions options)
        : this(httpClient, options, false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    private IppPrinterStatusClient(HttpClient httpClient, IppTransportOptions options, bool ownsClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options;
        _ownsClient = ownsClient;
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

    /// <summary>
    /// Queries identity and status details of a network printer via IPP Get-Printer-Attributes.
    /// </summary>
    /// <param name="host">The printer host name or IP address.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <param name="port">The IPP port. Defaults to 631. Note this is independent of any raw print channel port.</param>
    /// <param name="resourcePath">An extra resource path to try before the well-known ones, for example the <c>rp</c> attribute of a DNS-SD TXT record. Printers that use another path are then reachable.</param>
    /// <returns>The printer details.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    /// <exception cref="TimeoutException">Thrown when the printer does not answer in time.</exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">Thrown when the TLS handshake fails and this client validates certificates.</exception>
    /// <exception cref="ObjectDisposedException">Thrown after <see cref="Dispose"/>.</exception>
    public Task<IppPrinterDetails> GetDetailsAsync(string host, CancellationToken cancellationToken, int port = DefaultPort, string? resourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var resolver = _resolvers.GetOrAdd(
            $"{host}:{port}/{resourcePath}",
            _ => new IppEndpointResolver(_httpClient, host, port, resourcePath, _options));
        return resolver.RunAsync((uri, token) => ReadDetailsAsync(uri, PrinterId.FromNetwork(host), token), cancellationToken);
    }

    private async Task<IppPrinterDetails> ReadDetailsAsync(Uri uri, PrinterId id, CancellationToken cancellationToken)
    {
        IppOperations operations = new(_httpClient);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppStatusMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        return IppStatusMapper.Map(id, response.PrinterAttributes, operations.LastRawResponse);
    }
}
