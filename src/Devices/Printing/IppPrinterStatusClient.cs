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
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with an
    /// internally managed <see cref="HttpClient"/> that accepts any server certificate,
    /// because network printers overwhelmingly use self-signed certificates.
    /// Supply your own client for custom certificate validation.
    /// </summary>
    public IppPrinterStatusClient()
        : this(CreateDefaultClient(), true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterStatusClient"/> class with a
    /// caller-provided <see cref="HttpClient"/>. The client is not disposed by this instance.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send IPP requests.</param>
    public IppPrinterStatusClient(HttpClient httpClient)
        : this(httpClient, false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
    }

    private IppPrinterStatusClient(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    private static HttpClient CreateDefaultClient()
    {
        SocketsHttpHandler handler = new();
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return new HttpClient(handler, true);
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
    public async Task<IppPrinterDetails> GetDetailsAsync(string host, CancellationToken cancellationToken, int port = DefaultPort, string? resourcePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        IppEndpointResolver resolver = new(_httpClient, host, port, resourcePath);
        var uri = await resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        IppOperations operations = new(_httpClient);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppStatusMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        return IppStatusMapper.Map(PrinterId.FromNetwork(host), response.PrinterAttributes, operations.LastRawResponse);
    }
}
