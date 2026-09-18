using System.Linq;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.Printing.Spooler;

// CUPS is an IPP server, so this driver is the same IPP calls pointed at a fixed path.
// The path never changes, so IppEndpointResolver has nothing to probe. The daemon is on
// localhost for the spooler of Linux and macOS, and on another host for a CUPS server the
// application named; _server is what tells the two apart, because a queue of a named
// server is a cups:// channel and a local one is a spooler:// channel.
internal sealed class CupsSpoolerDriver : ISpoolerDriver
{
    private static readonly Uri DefaultBaseUri = new("ipp://localhost:631/");

    // One client for every driver: the target is fixed, and a client per driver would
    // leak a connection pool each time the factory makes one.
    private static readonly Lazy<HttpClient> SharedClient = new(static () => IppHttpClientFactory.Create(new IppTransportOptions()));

    private readonly IppContext _context;
    private readonly Uri _baseUri;
    private readonly PrintFormatPolicy _formats;

    // Null for the local daemon, which the operating system spooler addresses by queue
    // name alone.
    private readonly (string Host, int Port)? _server;

    public CupsSpoolerDriver(PrintFormatPolicy? formats = null, IppTransportOptions? options = null)
        : this(SharedClient.Value, DefaultBaseUri, formats, options)
    {
    }

    public CupsSpoolerDriver(HttpClient httpClient)
        : this(httpClient, DefaultBaseUri, null, null)
    {
    }

    // The one place a remote CUPS server is turned into a driver, so the three types that
    // reach one build the same thing from the same address.
    public static CupsSpoolerDriver ForServer(
        string host,
        int port,
        HttpClient httpClient,
        IppTransportOptions? options,
        PrintFormatPolicy? formats) =>
        new(httpClient, CupsPrinterEndpoint.ServerUri(host, port, options), formats, options, (host, port));

    internal CupsSpoolerDriver(
        HttpClient httpClient,
        Uri baseUri,
        PrintFormatPolicy? formats = null,
        IppTransportOptions? options = null,
        (string Host, int Port)? server = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);

        // The whole policy, not only the log: the raw-response switch must reach a CUPS
        // queue as well, because that is the channel a stopped spooler job runs on.
        _context = new IppContext(httpClient, options);
        _baseUri = baseUri;
        _formats = formats ?? PrintFormatPolicy.Default;
        _server = server;
    }

    // Every identifier this driver hands out, so a queue of a named server never reports
    // itself as a queue of the local spooler.
    private PrinterId IdFor(string queueName) =>
        _server is null
            ? PrinterId.ForSpooler(queueName)
            : PrinterId.ForCups(_server.Value.Host, queueName, _server.Value.Port);

    private PrinterEndpoint EndpointFor(string queueName) =>
        _server is null
            ? new SpoolerPrinterEndpoint(queueName)
            : new CupsPrinterEndpoint(_server.Value.Host, queueName, _server.Value.Port);

    public async Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        IppOperations operations = new(_context, _baseUri, "CUPS-Get-Printers");
        CUPSGetPrintersRequest request = new()
        {
            OperationAttributes = new() { PrinterUri = _baseUri },
        };
        var response = await operations.SendAsync(
            static (client, message, token) => client.GetCUPSPrintersAsync(message, token),
            request,
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

    // The name is taken and the address is built from the server this driver was pointed
    // at. printer-uri-supported is deliberately ignored: a CUPS server answers it with the
    // host name it believes it has, which is often not one the client can reach.
    private DiscoveredPrinter MapDiscovered(PrinterDescriptionAttributes attributes, string? deviceUri)
    {
        var name = attributes.PrinterName!;
        var id = IdFor(name);
        PrinterInfo info = new(id, attributes.PrinterInfo ?? name)
        {
            Location = attributes.PrinterLocation,
        };
        return new DiscoveredPrinter(id, EndpointFor(name), info)
        {
            Source = _server is null ? DiscoverySource.Spooler : DiscoverySource.CupsServer,
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
            _context,
            QueueUri(queueName),
            IdFor(queueName),
            new IppSubmission(payload, IppDocumentFormat.ForCups(payload.ContentType, _formats), options, []),
            cancellationToken);
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetStatusAsync(_context, QueueUri(queueName), IdFor(queueName), cancellationToken);
    }

    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetConfigurationAsync(_context, QueueUri(queueName), IdFor(queueName), cancellationToken);
    }

    public async Task<PrinterIdentity?> GetIdentityAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        var identity = await IppRequests.GetQueueIdentityAsync(_context, QueueUri(queueName), cancellationToken).ConfigureAwait(false);
        return identity.IsEmpty ? null : identity;
    }

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobsAsync(_context, QueueUri(queueName), IdFor(queueName), cancellationToken);
    }

    public Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.GetJobAsync(_context, QueueUri(queueName), IdFor(queueName), jobId, cancellationToken);
    }

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        return IppRequests.CancelJobAsync(_context, QueueUri(queueName), jobId, cancellationToken);
    }

    // The escape also covers a name handed to this driver directly, without an endpoint.
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{Uri.EscapeDataString(queueName)}");
}
