using System.Linq;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Spooler;

// CUPS is an IPP server on localhost:631, so this driver is the same IPP calls pointed
// at a fixed path. The path never changes, so IppEndpointResolver has nothing to probe.
internal sealed class CupsSpoolerDriver : ISpoolerDriver
{
    private static readonly Uri DefaultBaseUri = new("ipp://localhost:631/");

    // One client for every driver: the target is fixed, and a client per driver would
    // leak a connection pool each time the factory makes one.
    private static readonly Lazy<HttpClient> SharedClient = new(static () => IppHttpClientFactory.Create(new IppTransportOptions()));

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public CupsSpoolerDriver()
        : this(SharedClient.Value, DefaultBaseUri)
    {
    }

    public CupsSpoolerDriver(HttpClient httpClient)
        : this(httpClient, DefaultBaseUri)
    {
    }

    internal CupsSpoolerDriver(HttpClient httpClient, Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        _httpClient = httpClient;
        _baseUri = baseUri;
    }

    public async Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        IppOperations operations = new(_httpClient);
        CUPSGetPrintersRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = _baseUri },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetCUPSPrintersAsync(message, token),
            request,
            _baseUri,
            cancellationToken).ConfigureAwait(false);

        var attributes = response.PrintersAttributes ?? [];
        var raw = operations.LastRawResponse;
        List<DiscoveredPrinter> printers = new(attributes.Length);

        // An indexed loop, not a Where: the raw groups line up with the typed attributes
        // by position, and skipping an entry with a filter would shift every one after it.
        for (var i = 0; i < attributes.Length; i++)
        {
            if (String.IsNullOrWhiteSpace(attributes[i].PrinterName))
            {
                continue;
            }

            printers.Add(MapDiscovered(attributes[i], IppRawAttributes.ReadText(raw, i, "device-uri")));
        }

        return printers;
    }

    private static DiscoveredPrinter MapDiscovered(PrinterDescriptionAttributes attributes, string? deviceUri)
    {
        var name = attributes.PrinterName!;
        var id = PrinterId.ForSpooler(name);
        SpoolerPrinterEndpoint endpoint = new(name);
        PrinterInfo info = new(id, attributes.PrinterInfo ?? name)
        {
            Location = attributes.PrinterLocation,
        };
        return new DiscoveredPrinter(id, endpoint, info)
        {
            Source = DiscoverySource.Spooler,
            Aliases = SpoolerAliases.FromDeviceUri(deviceUri),
        };
    }

    // The options are validated above this driver, so nothing is dropped here.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);
        // The daemon is CUPS by construction, so the format needs no negotiation.
        return IppRequests.SubmitAsync(
            _httpClient,
            QueueUri(queueName),
            PrinterId.ForSpooler(queueName),
            new IppSubmission(payload, IppDocumentFormat.ForCups(payload.ContentType), options, []),
            cancellationToken);
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetStatusAsync(_httpClient, QueueUri(queueName), PrinterId.ForSpooler(queueName), cancellationToken);
    }

    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetConfigurationAsync(_httpClient, QueueUri(queueName), PrinterId.ForSpooler(queueName), cancellationToken);
    }

    public async Task<PrinterIdentity?> GetIdentityAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        var identity = await IppRequests.GetQueueIdentityAsync(_httpClient, QueueUri(queueName), cancellationToken).ConfigureAwait(false);
        return identity.IsEmpty ? null : identity;
    }

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobsAsync(_httpClient, QueueUri(queueName), PrinterId.ForSpooler(queueName), cancellationToken);
    }

    public Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobAsync(_httpClient, QueueUri(queueName), PrinterId.ForSpooler(queueName), jobId, cancellationToken);
    }

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.CancelJobAsync(_httpClient, QueueUri(queueName), jobId, cancellationToken);
    }

    // The escape also covers a name handed to this driver directly, without an endpoint.
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{Uri.EscapeDataString(queueName)}");
}
