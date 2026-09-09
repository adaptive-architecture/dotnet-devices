using System.Globalization;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

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

        using MemoryStream document = new(payload.Data.ToArray());
        PrintJobRequest request = new()
        {
            Document = document,
            OperationAttributes = new()
            {
                PrinterUri = uri,
                DocumentFormat = new DocumentFormat(payload.ContentType, true),
                JobName = effectiveOptions?.JobName,
            },
            JobTemplateAttributes = IppJobTemplateMapper.Map(effectiveOptions),
        };

        IppOperations operations = new(_httpClient);
        var response = await operations.SendAsync(
            static (client, message, token) => client.PrintJobAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var job = response.JobAttributes
            ?? throw new InvalidDataException($"The IPP response from '{uri}' did not include job attributes.");
        PrintJobInfo info = new(job.JobId.ToString(CultureInfo.InvariantCulture), Id, IppJobStateMapper.Map(job.JobState))
        {
            JobName = effectiveOptions?.JobName,
            DroppedOptions = dropped,
        };
        return info;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when no IPP endpoint answers, or the printer reports an IPP error.</exception>
    /// <exception cref="InvalidDataException">Thrown when the printer returns a malformed IPP response.</exception>
    public async Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var uri = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
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

        return IppStatusMapper.Map(Id, response.PrinterAttributes, operations.LastRawResponse).Status;
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
        IppOperations operations = new(_httpClient);
        GetPrinterAttributesRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = uri, RequestedAttributes = IppConfigurationMapper.RequestedAttributes },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetPrinterAttributesAsync(message, token),
            request,
            uri,
            cancellationToken).ConfigureAwait(false);

        var configuration = IppConfigurationMapper.Map(Id, response.PrinterAttributes);
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
