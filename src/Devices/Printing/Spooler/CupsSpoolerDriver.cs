using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Spooler;

// Reaches the CUPS daemon over IPP. CUPS is itself an IPP server listening on
// localhost:631, so this driver needs no native interop: it is the same IPP request
// and response types IppPrinter and IppPrintJobQueue already use, pointed at the
// fixed local daemon path instead of a resolved network endpoint. The daemon path
// never changes, so this driver does not use IppEndpointResolver: there is nothing
// to probe, and probing would double every call's round trips.
internal sealed class CupsSpoolerDriver : ISpoolerDriver
{
    private static readonly Uri DefaultBaseUri = new("ipp://localhost:631/");

    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    // Owns the client it builds here, because nothing else reaches the local CUPS
    // daemon and nothing else can share it.
    public CupsSpoolerDriver()
        : this(new HttpClient(), DefaultBaseUri)
    {
    }

    public CupsSpoolerDriver(HttpClient httpClient)
        : this(httpClient, DefaultBaseUri)
    {
    }

    // Lets a test point this driver at a stub server instead of the real daemon.
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
        foreach (var printer in attributes)
        {
            // A queue with no name cannot be addressed, so it is not reported.
            if (!String.IsNullOrWhiteSpace(printer.PrinterName))
            {
                printers.Add(MapDiscovered(printer));
            }
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
        return new DiscoveredPrinter(id, endpoint, info);
    }

    // The document format follows the payload content type, as IppPrinter does. CUPS
    // accepts application/octet-stream and applies its own filter chain from there.
    // A spooler printer validates the options above this driver, so nothing is dropped here.
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

    // "printers/lobby" against "ipp://localhost:631/" gives "ipp://localhost:631/printers/lobby".
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{queueName}");
}
