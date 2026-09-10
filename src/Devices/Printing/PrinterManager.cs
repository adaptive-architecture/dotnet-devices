using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Default <see cref="IPrinterManager"/>. Runs every configured discovery source
/// together and reports the devices they found.
/// </summary>
/// <remarks>
/// Discovery has three phases. Every source runs at once and reports the channels it
/// found. When the caller asked for it, each channel is then opened once and asked what
/// it supports and which device it belongs to. Finally the channels are grouped, so one
/// physical printer is one <see cref="PrinterDevice"/> however many ways it can be
/// reached.
/// <para>
/// A device usually has several channels, and each call picks the one it needs. A print
/// that requires passthrough takes the raw channel; every other call prefers a channel
/// with a job queue, so the job can be watched after it is sent.
/// </para>
/// </remarks>
public sealed class PrinterManager : IPrinterManager
{
    private readonly IMdnsPrinterDiscovery _mdns;
    private readonly IPrinterDiscovery _spooler;
    private readonly INetworkPrinterDiscovery _probe;
    private readonly IPrinterFactory _factory;
    private readonly IPrintJobMonitor _monitor;

    // Every key of a device maps to that device: its own key and each alias a source
    // vouched for. A caller that kept an old address identifier therefore still resolves
    // after the device has been recognised by its identity.
    private readonly ConcurrentDictionary<PrinterDeviceKey, PrinterDevice> _devices = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterManager"/> class.
    /// </summary>
    /// <param name="mdns">The multicast DNS discovery source.</param>
    /// <param name="spooler">The operating system print spooler discovery source.</param>
    /// <param name="probe">The direct network probe discovery source.</param>
    /// <param name="factory">The factory used to open a channel once found.</param>
    /// <param name="monitor">The monitor used to watch a job on a channel that has a job queue.</param>
    public PrinterManager(IMdnsPrinterDiscovery mdns, IPrinterDiscovery spooler, INetworkPrinterDiscovery probe, IPrinterFactory factory, IPrintJobMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(mdns);
        ArgumentNullException.ThrowIfNull(spooler);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(monitor);
        _mdns = mdns;
        _spooler = spooler;
        _probe = probe;
        _factory = factory;
        _monitor = monitor;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(PrinterManagerOptions? options, CancellationToken cancellationToken)
    {
        var effective = options ?? new PrinterManagerOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(effective.MaxEnrichmentConcurrency, 1);

        var channels = await FindAsync(effective, cancellationToken).ConfigureAwait(false);
        Dictionary<PrinterId, IReadOnlyList<PrinterStatusSource>> statusSources = [];
        if (effective.ReadIdentity || effective.ReadCapabilities)
        {
            channels = await EnrichAsync(channels, effective, statusSources, cancellationToken).ConfigureAwait(false);
        }

        var devices = PrinterDeviceGrouper.Group(channels, statusSources);
        foreach (var device in devices)
        {
            Remember(device);
        }

        return devices;
    }

    // Phase one. Every source runs at once, and a source that fails does not fail the
    // call: an empty list is an answer, so only an all-source failure throws.
    private async Task<IReadOnlyList<DiscoveredPrinter>> FindAsync(PrinterManagerOptions options, CancellationToken cancellationToken)
    {
        List<Task<(DiscoverySource Source, IReadOnlyList<DiscoveredPrinter> Printers, Exception? Error)>> running = [];
        if (options.IncludeMdns)
        {
            running.Add(RunAsync(DiscoverySource.Mdns, () => _mdns.DiscoverAsync(options.Mdns, cancellationToken), cancellationToken));
        }

        if (options.IncludeSpooler)
        {
            running.Add(RunAsync(DiscoverySource.Spooler, () => _spooler.DiscoverAsync(cancellationToken), cancellationToken));
        }

        if (options.Probe is not null)
        {
            running.Add(RunAsync(DiscoverySource.NetworkProbe, () => _probe.DiscoverAsync(options.Probe, cancellationToken), cancellationToken));
        }

        var results = await Task.WhenAll(running).ConfigureAwait(false);

        List<DiscoveredPrinter> found = [];
        Dictionary<DiscoverySource, Exception> failures = [];
        foreach (var result in results)
        {
            if (result.Error is null)
            {
                found.AddRange(result.Printers);
            }
            else
            {
                failures[result.Source] = result.Error;
            }
        }

        if (failures.Count > 0 && failures.Count == results.Length)
        {
            throw new PrinterDiscoveryException(failures);
        }

        // Two sources can report one channel, but every distinct channel is kept.
        return found.DistinctBy(static channel => (channel.Id, channel.Endpoint)).ToList();
    }

    // Phase two. Opens each channel once and asks it what it supports and which device it
    // is. It is opt-in because every answer costs a request. A channel that fails to
    // answer is left as it was: an enrichment must never fail a discovery that worked.
    private async Task<IReadOnlyList<DiscoveredPrinter>> EnrichAsync(
        IReadOnlyList<DiscoveredPrinter> channels,
        PrinterManagerOptions options,
        Dictionary<PrinterId, IReadOnlyList<PrinterStatusSource>> statusSources,
        CancellationToken cancellationToken)
    {
        var enriched = new DiscoveredPrinter[channels.Count];
        ParallelOptions parallel = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.MaxEnrichmentConcurrency,
        };

        Lock guard = new();
        await Parallel.ForAsync(0, channels.Count, parallel, async (index, token) =>
        {
            var channel = channels[index];
            var read = await ReadAsync(channel, options, token).ConfigureAwait(false);
            enriched[index] = read.Channel;
            if (read.Sources.Count > 0)
            {
                lock (guard)
                {
                    statusSources[read.Channel.Id] = read.Sources;
                }
            }
        }).ConfigureAwait(false);

        return enriched;
    }

    private async Task<(DiscoveredPrinter Channel, IReadOnlyList<PrinterStatusSource> Sources)> ReadAsync(
        DiscoveredPrinter channel,
        PrinterManagerOptions options,
        CancellationToken cancellationToken)
    {
        IPrinter? printer = null;
        try
        {
            // The open is inside the guard on purpose: a channel that cannot even be
            // opened must not fail a discovery that already succeeded.
            printer = _factory.Open(channel);

            PrinterConfiguration? configuration = null;
            if (options.ReadCapabilities)
            {
                configuration = await TryReadAsync<PrinterConfiguration>(async () => await printer.GetConfigurationAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            }

            PrinterIdentity? identity = null;
            if (options.ReadIdentity)
            {
                identity = await TryReadAsync(() => printer.GetIdentityAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            var sources = identity is null && configuration is null ? [] : SourcesOf(channel);
            return (Apply(channel, identity, configuration), sources);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The channel could not be opened or read. It stays as discovery found it.
            return (channel, []);
        }
        finally
        {
            if (printer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    // Only the caller's own cancellation fails a read: an HttpClient timeout also arrives
    // as TaskCanceledException, and must not hide what the printer did answer.
    private static async Task<T?> TryReadAsync<T>(Func<Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<PrinterStatusSource> SourcesOf(DiscoveredPrinter channel)
    {
        if (channel.Endpoint.Scheme is PrinterScheme.Ipp or PrinterScheme.Ipps)
        {
            return [PrinterStatusSource.Ipp];
        }

        if (channel.Endpoint.Scheme == PrinterScheme.Spooler)
        {
            return [PrinterStatusSource.Spooler];
        }

        // A raw channel reports nothing itself; what answered was the SNMP agent.
        return channel.Endpoint.Scheme == PrinterScheme.Raw ? [PrinterStatusSource.Snmp] : [];
    }

    // Folds what a channel reported into the channel, keeping the old key as an alias so
    // an identifier a caller already holds keeps resolving.
    internal static DiscoveredPrinter Apply(DiscoveredPrinter channel, PrinterIdentity? identity, PrinterConfiguration? configuration)
    {
        if (identity is null)
        {
            return configuration is null
                ? channel
                : channel.With(channel.Id, channel.Info, configuration, channel.Aliases);
        }

        List<PrinterDeviceKey> aliases = [.. channel.Aliases, channel.Id.DeviceKey];
        foreach (var alias in identity.Aliases)
        {
            aliases.Add(alias);
        }

        var uuid = identity.Uuid;
        if (PrinterDeviceKey.IsUsableIdentity(uuid))
        {
            aliases.Add(PrinterDeviceKey.ForDeviceIdentity(uuid!));
        }

        if (PrinterDeviceKey.IsUsableIdentity(identity.SerialNumber))
        {
            aliases.Add(PrinterDeviceKey.ForDeviceIdentity(identity.SerialNumber!));
        }

        PrinterInfo info = new(channel.Info.Id, PickName(channel, identity.Name))
        {
            Location = channel.Info.Location ?? identity.Location,
            DriverName = channel.Info.DriverName,
            IsDefault = channel.Info.IsDefault || identity.IsDefault,
            IsShared = channel.Info.IsShared || identity.IsShared,
            Uuid = channel.Info.Uuid ?? uuid,
            SerialNumber = channel.Info.SerialNumber ?? identity.SerialNumber,
            Manufacturer = channel.Info.Manufacturer ?? identity.Manufacturer,
            Model = channel.Info.Model ?? identity.Model ?? identity.MakeAndModel,
            CommandSets = channel.Info.CommandSets.Count > 0 ? channel.Info.CommandSets : identity.CommandSets,
        };

        // The identifier becomes the identity form only when the device named itself with
        // a UUID. A serial number reads like a host name, so writing one into the
        // authority would make the text ambiguous; it still groups the device.
        var id = PrinterId.TryParseDeviceUuid(uuid, out var parsed)
            ? PrinterId.ForDeviceUuid(channel.Endpoint.Scheme, parsed)
            : channel.Id;

        return channel.With(id, info, configuration, Distinct(aliases));
    }

    // A discovery that learned no name uses the address as one. That placeholder gives
    // way to a real name; a name a source actually reported does not.
    private static string PickName(DiscoveredPrinter channel, string? reported)
    {
        if (String.IsNullOrWhiteSpace(reported))
        {
            return channel.Info.Name;
        }

        var isPlaceholder = String.Equals(channel.Info.Name, channel.Id.Authority, StringComparison.OrdinalIgnoreCase);
        return isPlaceholder ? reported : channel.Info.Name;
    }

    private static IReadOnlyList<PrinterDeviceKey> Distinct(List<PrinterDeviceKey> keys)
    {
        List<PrinterDeviceKey> unique = new(keys.Count);
        foreach (var key in keys)
        {
            if (!unique.Contains(key))
            {
                unique.Add(key);
            }
        }

        return unique;
    }

    private void Remember(PrinterDevice device)
    {
        _devices[device.Key] = device;
        foreach (var channel in device.Channels)
        {
            _devices[channel.Id.DeviceKey] = device;
            foreach (var alias in channel.Aliases)
            {
                _devices[alias] = device;
            }
        }
    }

    /// <inheritdoc />
    public async Task<PrintJobInfo> PrintAsync(PrinterId id, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var device = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        var channel = Choose(device, id, options?.RequirePassthrough == true);
        var printer = _factory.Open(channel);
        try
        {
            return await printer.PrintAsync(payload, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (printer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public async Task<PrinterStatus> GetStatusAsync(PrinterId id, CancellationToken cancellationToken)
    {
        var device = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        var printer = _factory.Open(Choose(device, id, false));
        try
        {
            return await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (printer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId id,
        string jobId,
        PrintJobMonitorOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(options);

        return WatchJobAsyncCore(id, jobId, options, cancellationToken);
    }

    // The guards live in the public method, so they throw before the first enumeration.
    private async IAsyncEnumerable<PrintJobInfo> WatchJobAsyncCore(
        PrinterId id,
        string jobId,
        PrintJobMonitorOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var device = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        if (!device.HasJobQueue)
        {
            throw new NotSupportedException(
                $"Printer '{id}' has no job queue, so a job sent to it cannot be watched. " +
                "A raw channel gives back no job identifier, and the job was reported complete when it was submitted. " +
                $"Its endpoints are {String.Join(", ", device.Channels.Select(static channel => channel.Endpoint))}.");
        }

        // The job queue is addressed, not the device: an identity names no host, so the
        // endpoint of the chosen channel is what the queue can be opened from.
        var channel = Choose(device, id, false);
        await foreach (var reading in _monitor.WatchJobAsync(PrinterId.FromEndpoint(channel.Endpoint), jobId, options, cancellationToken).ConfigureAwait(false))
        {
            yield return reading;
        }
    }

    // The channel the caller named is used when it does what the call needs; otherwise
    // another channel of the same device is. A device found through the spooler can
    // therefore still be printed to over its raw channel.
    private static DiscoveredPrinter Choose(PrinterDevice device, PrinterId id, bool requirePassthrough)
    {
        var named = device.Channels.FirstOrDefault(channel => channel.Id == id);
        if (requirePassthrough)
        {
            if (named is not null && named.GivesPassthrough)
            {
                return named;
            }

            return device.Channels.FirstOrDefault(static channel => channel.GivesPassthrough)
                ?? throw new NotSupportedException(
                    $"Printer '{id}' has no channel that sends the payload unchanged, and the options require one. " +
                    $"Its endpoints are {String.Join(", ", device.Channels.Select(static channel => channel.Endpoint))}.");
        }

        if (named is not null && named.HasJobQueue)
        {
            return named;
        }

        return device.Channels.FirstOrDefault(static channel => channel.HasJobQueue) ?? named ?? device.Channels[0];
    }

    // An address form names its own endpoint, so a miss opens it directly and nothing is
    // browsed. An identity form names no address and runs one fresh discovery.
    private async Task<PrinterDevice> ResolveAsync(PrinterId id, CancellationToken cancellationToken)
    {
        if (_devices.TryGetValue(id.DeviceKey, out var cached))
        {
            return cached;
        }

        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have filled the cache while this one waited.
            if (_devices.TryGetValue(id.DeviceKey, out cached))
            {
                return cached;
            }

            if (id.TryCreateEndpoint(out var endpoint) && endpoint is not null)
            {
                DiscoveredPrinter channel = new(id, endpoint, new PrinterInfo(id, id.Authority));
                PrinterDevice device = new(id.DeviceKey, [channel]);
                Remember(device);
                return device;
            }

            _ = await DiscoverAsync(null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _refresh.Release();
        }

        return _devices.TryGetValue(id.DeviceKey, out var found)
            ? found
            : throw new InvalidOperationException($"No printer with the identifier '{id}' was found, and a fresh discovery did not find one either.");
    }

    // Only the caller's own cancellation fails the whole call: an HttpClient timeout also
    // arrives as TaskCanceledException, and must not hide what the other sources found.
    private static async Task<(DiscoverySource Source, IReadOnlyList<DiscoveredPrinter> Printers, Exception? Error)> RunAsync(
        DiscoverySource source, Func<Task<IReadOnlyList<DiscoveredPrinter>>> discover, CancellationToken cancellationToken)
    {
        try
        {
            return (source, await discover().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (source, [], exception);
        }
    }
}
