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
        List<DiscoveredPrinter> printers = new(attributes.Length);
        foreach (var printer in attributes.Where(printer => !String.IsNullOrWhiteSpace(printer.PrinterName)))
        {
            printers.Add(MapDiscovered(printer));
        }

        return printers;
    }

    private static DiscoveredPrinter MapDiscovered(PrinterDescriptionAttributes attributes)
    {
        var name = attributes.PrinterName!;
        var id = PrinterId.FromSpooler(name);
        SpoolerPrinterEndpoint endpoint = new(name);
        PrinterInfo info = new(id, attributes.PrinterInfo ?? name)
        {
            Location = attributes.PrinterLocation,
        };
        return new DiscoveredPrinter(id, endpoint, info) { Source = DiscoverySource.Spooler };
    }

    // The options are validated above this driver, so nothing is dropped here.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);
        return IppRequests.SubmitAsync(_httpClient, QueueUri(queueName), PrinterId.FromSpooler(queueName), payload, options, [], cancellationToken);
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetStatusAsync(_httpClient, QueueUri(queueName), PrinterId.FromSpooler(queueName), cancellationToken);
    }

    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetConfigurationAsync(_httpClient, QueueUri(queueName), PrinterId.FromSpooler(queueName), cancellationToken);
    }

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobsAsync(_httpClient, QueueUri(queueName), PrinterId.FromSpooler(queueName), cancellationToken);
    }

    public Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobAsync(_httpClient, QueueUri(queueName), PrinterId.FromSpooler(queueName), jobId, cancellationToken);
    }

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.CancelJobAsync(_httpClient, QueueUri(queueName), jobId, cancellationToken);
    }

    // The escape also covers a name handed to this driver directly, without an endpoint.
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{Uri.EscapeDataString(queueName)}");
}
