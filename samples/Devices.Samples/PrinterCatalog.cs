using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// One discovery serves every request, as one discovery served the whole console menu. A
// browse costs 30 seconds and a subnet probe costs minutes, so neither runs again until
// the person asks for it.
internal sealed class PrinterCatalog
{
    private readonly IPrinterManager _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<PrinterDevice> _devices = [];
    private bool _discovered;

    public PrinterCatalog(IPrinterManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    // The last result, or a first discovery. Two browsers that press Refresh together must
    // not both pay for the browse, so the gate lets one through and the other reads what it
    // found.
    public async Task<IReadOnlyList<PrinterDevice>> GetAsync(bool refresh, bool probe, CancellationToken cancellationToken)
    {
        if (_discovered && !refresh && !probe)
        {
            return _devices;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_discovered && !refresh && !probe)
            {
                return _devices;
            }

            var found = await BrowseAsync(cancellationToken).ConfigureAwait(false);
            if (found.Count == 0 && probe)
            {
                found = await ProbeAsync(cancellationToken).ConfigureAwait(false);
            }

            _devices = found;
            _discovered = true;
            return _devices;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    // Both reads are opt-in because each costs one request per channel. They are what
    // merges the channels of one printer and fills in its capabilities.
    private async Task<IReadOnlyList<PrinterDevice>> BrowseAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(30));
        PrinterManagerOptions options = new() { ReadIdentity = true, ReadCapabilities = true };
        try
        {
            return await _manager.DiscoverAsync(options, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PrinterDiscoveryException or OperationCanceledException)
        {
            return [];
        }
    }

    // Nothing advertised itself, so probe every local address on TCP port 9100.
    private async Task<IReadOnlyList<PrinterDevice>> ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromMinutes(2));
        PrinterManagerOptions options = new()
        {
            // The caller that falls back here already browsed and found nothing.
            IncludeMdns = false,
            IncludeSpooler = false,
            Probe = new NetworkPrinterDiscoveryOptions
            {
                Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts(),
                ConnectTimeout = TimeSpan.FromMilliseconds(500),
                MaxDegreeOfParallelism = 64,
            },
            ReadIdentity = true,
            ReadCapabilities = true,
        };

        try
        {
            return await _manager.DiscoverAsync(options, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is PrinterDiscoveryException or OperationCanceledException)
        {
            return [];
        }
    }

    // A printer identifier names a channel, or the device. Both must resolve, because the
    // page sends back whichever identifier it showed.
    public bool TryFind(PrinterId id, out PrinterDevice device, out DiscoveredPrinter channel)
    {
        var text = id.ToString();
        foreach (var candidate in _devices)
        {
            foreach (var reachable in candidate.Channels)
            {
                if (String.Equals(reachable.Id.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    device = candidate;
                    channel = reachable;
                    return true;
                }
            }

            if (String.Equals(candidate.Id.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                device = candidate;
                channel = candidate.Channels.Count > 0 ? candidate.Channels[0] : null;
                return true;
            }
        }

        device = null;
        channel = null;
        return false;
    }

    // A channel with no job queue reports the job done as soon as it took the bytes, so
    // there is nothing to watch. An identifier that is not in the catalog is treated as a
    // queue: the watch then reports the real reason it cannot follow the job.
    public bool HasJobQueue(PrinterId id) =>
        !TryFind(id, out _, out var channel) || channel is null || channel.HasJobQueue;

    public AcceptsDto Accepts(PrinterId id, string contentType)
    {
        if (!TryFind(id, out var device, out var channel) || channel is null)
        {
            return new AcceptsDto(null, null);
        }

        return new AcceptsDto(device.Accepts(channel, contentType), Reads(channel));
    }

    // The list the channel does read is the useful next step, so it travels with the
    // refusal instead of leaving the person to go and look it up.
    internal static string Reads(DiscoveredPrinter channel)
    {
        if (!String.IsNullOrWhiteSpace(channel.Info.DriverName))
        {
            return channel.Info.DriverName;
        }

        var formats = channel.Configuration?.SupportedDocumentFormats;
        return formats is null || formats.Count == 0 ? null : String.Join(", ", formats);
    }
}
