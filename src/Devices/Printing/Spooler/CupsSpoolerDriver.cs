using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing.Ipp;
using Microsoft.Extensions.Logging;
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

    // CUPS_PTYPE_DEFAULT: the server sets it in printer-type on its default queue.
    private const int PrinterTypeDefault = 0x20000;

    // A CUPS job attribute that no IPP specification defines; see FitsOntoQueueMedia.
    private static readonly IppAttribute FitToPage = new(Tag.Boolean, "fit-to-page", true);

    // One client for every driver of one policy: the target is fixed, and a client per
    // driver would leak a connection pool each time the factory makes one. The connect
    // timeout, the credentials and the certificate trust live in the handler, so a policy
    // needs a client of its own; the table is weakly keyed, so a policy that goes away
    // takes its pool with it. Every client connects through the daemon's domain socket
    // where there is one; see CupsLocalSocket.
    private static readonly Lazy<HttpClient> SharedClient = new(static () => CupsLocalSocket.CreateClient(new IppTransportOptions()));
    private static readonly ConditionalWeakTable<IppTransportOptions, HttpClient> ClientsByOptions = [];

    private readonly IppContext _context;
    private readonly Uri _baseUri;
    private readonly PrintFormatPolicy _formats;

    // Null for the local daemon, which the operating system spooler addresses by queue
    // name alone.
    private readonly (string Host, int Port)? _server;

    // The default a user set locally, which lp prefers over the server's. Null where only
    // the server default applies.
    private readonly Func<string?>? _userDefault;

    private readonly ConcurrentDictionary<string, PrinterConfiguration> _configurations = new(StringComparer.Ordinal);

    public CupsSpoolerDriver(PrintFormatPolicy? formats = null, IppTransportOptions? options = null, ILoggerFactory? loggerFactory = null)
        : this(ClientFor(options), DefaultBaseUri, formats, options, userDefault: CupsUserDefault.Find, loggerFactory: loggerFactory)
    {
    }

    internal static HttpClient ClientFor(IppTransportOptions? options) =>
        options is null ? SharedClient.Value : ClientsByOptions.GetValue(options, CupsLocalSocket.CreateClient);

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
        (string Host, int Port)? server = null,
        Func<string?>? userDefault = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);

        // The whole policy, not only the log: the raw-response switch must reach a CUPS
        // queue as well, because that is the channel a stopped spooler job runs on.
        _context = new IppContext(httpClient, options, loggerFactory);
        _baseUri = baseUri;
        _formats = formats ?? PrintFormatPolicy.Default;
        _server = server;
        _userDefault = userDefault;
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
        var userDefault = _userDefault?.Invoke();

        // An indexed loop, not a Where: the raw groups line up with the typed attributes
        // by position, and skipping an entry with a filter would shift every one after it.
        for (var i = 0; i < attributes.Length; i++)
        {
            if (String.IsNullOrWhiteSpace(attributes[i].PrinterName))
            {
                continue;
            }

            // A user default that names no queue marks none, as lp then fails rather than
            // falling back to the server.
            var isDefault = userDefault is null
                ? (IppRawAttributes.ReadInteger(raw, i, "printer-type") & PrinterTypeDefault) is > 0
                : String.Equals(userDefault, attributes[i].PrinterName, StringComparison.Ordinal);
            printers.Add(MapDiscovered(attributes[i], IppRawAttributes.ReadText(raw, i, "device-uri"), isDefault));
        }

        return printers;
    }

    // The name is taken and the address is built from the server this driver was pointed
    // at. printer-uri-supported is deliberately ignored: a CUPS server answers it with the
    // host name it believes it has, which is often not one the client can reach.
    private DiscoveredPrinter MapDiscovered(PrinterDescriptionAttributes attributes, string? deviceUri, bool isDefault)
    {
        var name = attributes.PrinterName!;
        var id = IdFor(name);
        PrinterInfo info = new(id, attributes.PrinterInfo ?? name)
        {
            Location = attributes.PrinterLocation,
            IsDefault = isDefault,
        };
        return new DiscoveredPrinter(id, EndpointFor(name), info)
        {
            Source = _server is null ? DiscoverySource.Spooler : DiscoverySource.CupsServer,
            Aliases = SpoolerAliases.FromDeviceUri(deviceUri),
        };
    }

    // A document is rendered by the library when the job asks for an engine or for geometry
    // no IPP attribute carries, and by the CUPS filters otherwise; the rest of the options
    // were validated above this driver.
    public async Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        var uri = QueueUri(queueName);
        var id = IdFor(queueName);
        IppConversionChannel channel = new(
            _formats,
            id,
            _context.Logger,
            token => GetConversionConfigurationAsync(queueName, token),
            _ => Task.FromResult(uri),
            TargetsFor(_server is null && OperatingSystem.IsMacOS()))
        {
            ReadsEveryDocument = true,
        };

        // The daemon is CUPS by construction, so the format needs no negotiation.
        var conversion = await IppDocumentConversion.ConvertIfNeededAsync(
            channel,
            payload,
            IppDocumentFormat.ForCups(payload.ContentType, _formats),
            options,
            cancellationToken).ConfigureAwait(false);
        PrintOptionValidator.ThrowIfRefused(options, id, conversion.Dropped);

        var effectiveOptions = conversion.Options;
        IReadOnlyList<IppAttribute> extras = [];
        if (!conversion.IsConverted && !conversion.PlacedOnMedia && FitsOntoQueueMedia(payload, options))
        {
            var defaultMedia = options?.MediaSize is null && options?.MediaDimensions is null
                ? await IppRequests.GetDefaultMediaAsync(_context, uri, id, cancellationToken).ConfigureAwait(false)
                : null;
            effectiveOptions = PrintOptionValidator.WithQueueFit(conversion.Options, defaultMedia);
            extras = [FitToPage];
        }

        return await IppRequests.SubmitAsync(
            _context,
            uri,
            id,
            new IppSubmission(conversion.Payload, conversion.Format, effectiveOptions, conversion.Dropped)
            {
                ExtraJobAttributes = extras,
                ConverterUsed = conversion.ConverterUsed,
            },
            cancellationToken).ConfigureAwait(false);
    }

    // A macOS queue lists PWG Raster and then fails every PWG Raster job in a filter, so on
    // the local daemon of a Mac it is never a target. A remote server does not say what it
    // runs on, so it is offered both, URF first.
    internal static string[] TargetsFor(bool isLocalMacOs) =>
        isLocalMacOs ? [PrinterContentTypes.Urf] : IppDocumentFormat.CupsTargets;

    // The formats and raster attributes of a queue change only when an administrator edits
    // it, so a rendered job reads them once for each queue.
    private async Task<PrinterConfiguration> GetConversionConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        if (_configurations.TryGetValue(queueName, out var cached))
        {
            return cached;
        }

        var configuration = await GetConfigurationAsync(queueName, cancellationToken).ConfigureAwait(false);
        return _configurations.GetOrAdd(queueName, configuration);
    }

    // A document CUPS renders lands where the renderer puts it, and the renderers disagree.
    // cups-filters on Linux centres the page on the queue's default media and shrinks it only
    // when it does not fit, which is what the Windows spooler does too. Quartz on macOS makes
    // the PDF's own page size the media when the job names none, so a label reaches a printer
    // loaded with A4 as a label-sized page and the printer reports a size mismatch; told the
    // media, it draws from the corner and ignores print-scaling. Only "fit-to-page" makes it
    // centre, and it fits exactly as "auto" does. So the job names the media, print-scaling for
    // cups-filters, which reads it before fit-to-page, and fit-to-page for Quartz.
    // A page that is its own media is left alone: with no media named, Quartz prints it at its
    // own size, which is what the job asked for.
    private bool FitsOntoQueueMedia(PrinterPayload payload, PrintOptions? options) =>
        _formats.KindOf(payload.ContentType) == PrinterFormatKind.Document
        && options?.MediaSizeSource != MediaSizeSource.Document;

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
        return IppRequests.CancelJobAsync(_context, QueueUri(queueName), jobId, PrintOptions.EffectiveUserName(null), cancellationToken);
    }

    // The escape also covers a name handed to this driver directly, without an endpoint.
    private Uri QueueUri(string queueName) => new(_baseUri, $"printers/{Uri.EscapeDataString(queueName)}");
}
