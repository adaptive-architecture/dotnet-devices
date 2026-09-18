using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing.Ipp;
using Microsoft.Extensions.Logging;

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
/// of a printer language takes a channel that sends the bytes unchanged; every other call
/// prefers a channel with a job queue, so the job can be watched after it is sent.
/// </para>
/// </remarks>
public sealed class PrinterManager : IPrinterManager
{
    private readonly IMdnsPrinterDiscovery _mdns;
    private readonly IPrinterDiscovery _spooler;
    private readonly INetworkPrinterDiscovery _probe;
    private readonly IPrinterFactory _factory;
    private readonly IPrintJobMonitor _monitor;
    private readonly PrinterManagerOptions _options;
    private readonly PrintFormatPolicy _formats;
    private readonly ILogger _logger;

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
        : this(mdns, spooler, probe, factory, monitor, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterManager"/> class with the policy
    /// it keeps for every call.
    /// </summary>
    /// <param name="mdns">The multicast DNS discovery source.</param>
    /// <param name="spooler">The operating system print spooler discovery source.</param>
    /// <param name="probe">The direct network probe discovery source.</param>
    /// <param name="factory">The factory used to open a channel once found.</param>
    /// <param name="monitor">The monitor used to watch a job on a channel that has a job queue.</param>
    /// <param name="options">
    /// The policy of this manager: the transports it may open, and the discovery scope a
    /// call to <see cref="DiscoverAsync(PrinterManagerOptions?, CancellationToken)"/> uses
    /// when it is given no options of its own.
    /// When <c>null</c>, the defaults apply.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="options"/> allows no transport.</exception>
    public PrinterManager(
        IMdnsPrinterDiscovery mdns,
        IPrinterDiscovery spooler,
        INetworkPrinterDiscovery probe,
        IPrinterFactory factory,
        IPrintJobMonitor monitor,
        PrinterManagerOptions? options)
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
        _options = options ?? new PrinterManagerOptions();

        // One snapshot for the life of the manager, so a list edited later does not change
        // how a job that is already on its way is routed.
        _formats = _options.BuildFormatPolicy();
        _logger = PrintingLog.Create(_options.LoggerFactory);
        if (_options.Transports.Count == 0)
        {
            throw new ArgumentException("A manager that may open no transport can print nothing.", nameof(options));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(PrinterManagerOptions? options, CancellationToken cancellationToken) =>
        DiscoverAsync(options, true, cancellationToken);

    // allowTracer is false on the path that refreshes to resolve an identifier. A print to
    // an identifier the cache does not hold triggers a discovery, and a tracer job there
    // would leave a job on every idle printer on the network as a side effect of printing
    // one label.
    private async Task<IReadOnlyList<PrinterDevice>> DiscoverAsync(PrinterManagerOptions? options, bool allowTracer, CancellationToken cancellationToken)
    {
        var effective = options ?? _options;
        ArgumentOutOfRangeException.ThrowIfLessThan(effective.MaxEnrichmentConcurrency, 1);

        var stopwatch = Stopwatch.StartNew();
        var channels = await FindAsync(effective, cancellationToken).ConfigureAwait(false);
        Dictionary<PrinterId, IReadOnlyList<PrinterStatusSource>> statusSources = [];
        if (effective.ReadIdentity || effective.ReadCapabilities)
        {
            channels = await EnrichAsync(channels, effective, statusSources, cancellationToken).ConfigureAwait(false);
        }

        // Read from the manager's own options and never from the argument, for the same
        // reason Transports is: the argument scopes one discovery, and consent to write to a
        // printer is not a scope.
        if (_options.QueueCorrelation is QueueCorrelationOptions correlation)
        {
            channels = await CorrelateAsync(channels, correlation, allowTracer, effective.MaxEnrichmentConcurrency, cancellationToken).ConfigureAwait(false);
        }

        var devices = PrinterDeviceGrouper.Group(channels, statusSources, _formats);
        foreach (var device in devices)
        {
            Remember(device);
        }

        PrintingLog.DiscoveryCompleted(_logger, devices.Count, channels.Count, stopwatch.ElapsedMilliseconds);
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
            // Debug, not Error: the exception below carries the failure of each source, and
            // one failure reported twice helps nobody.
            foreach ((var source, var error) in failures)
            {
                PrintingLog.EverySourceFailed(_logger, source, error);
            }

            throw new PrinterDiscoveryException(failures);
        }

        // Error: the caller gets the printers of the other sources and never learns that
        // one source found nothing because it failed.
        foreach ((var source, var error) in failures)
        {
            PrintingLog.DiscoverySourceFailed(_logger, source, results.Length - failures.Count, results.Length, error);
        }

        // Two sources can report one channel, but every distinct channel is kept.
        var kept = found.DistinctBy(static channel => (channel.Id, channel.Endpoint)).ToList();
        if (kept.Count != found.Count)
        {
            PrintingLog.ChannelsDeduplicated(_logger, found.Count, kept.Count);
        }

        return kept;
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

    // Phase three. Asks the channels no source named a device for what is in their queue,
    // and merges the ones that answer with the same jobs. Opt-in, and opt-in again before it
    // writes anything.
    private async Task<IReadOnlyList<DiscoveredPrinter>> CorrelateAsync(
        IReadOnlyList<DiscoveredPrinter> channels,
        QueueCorrelationOptions correlation,
        bool allowTracer,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        List<IPrinter> opened = [];
        try
        {
            List<PrinterQueueCorrelator.Candidate> candidates = [];
            foreach (var channel in PrinterQueueCorrelator.Choose(channels, _options.Transports))
            {
                IPrinter printer;
                try
                {
                    printer = _factory.Open(channel);
                }
                catch (Exception exception)
                {
                    // One channel that cannot even be opened costs its own evidence and
                    // nothing else, the same way an enrichment read does.
                    PrintingLog.CorrelationOpenFailed(_logger, channel.Id, channel.Endpoint, exception);
                    continue;
                }

                opened.Add(printer);

                // A factory of the caller's own may return something that cannot answer
                // about a queue. That channel simply contributes no evidence.
                if (printer is IQueueEvidenceChannel evidence)
                {
                    candidates.Add(new PrinterQueueCorrelator.Candidate(channel, evidence));
                }
            }

            var proved = await PrinterQueueCorrelator
                .CorrelateAsync(candidates, correlation, allowTracer, maxConcurrency, _logger, cancellationToken)
                .ConfigureAwait(false);

            return proved.Count == 0 ? channels : ApplyAliases(channels, proved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A correlation that failed proves nothing. It must never fail a discovery that
            // already worked. Error: the caller gets ungrouped devices and is told nothing.
            PrintingLog.CorrelationFailed(_logger, channels.Count, exception);
            return channels;
        }
        finally
        {
            foreach (var printer in opened)
            {
                if (printer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    private static List<DiscoveredPrinter> ApplyAliases(
        IReadOnlyList<DiscoveredPrinter> channels,
        IReadOnlyDictionary<PrinterId, IReadOnlyList<PrinterDeviceKey>> proved)
    {
        List<DiscoveredPrinter> applied = new(channels.Count);
        foreach (var channel in channels)
        {
            if (!proved.TryGetValue(channel.Id, out var aliases))
            {
                applied.Add(channel);
                continue;
            }

            applied.Add(channel.WithAliases(Distinct([.. channel.Aliases, .. aliases])));
        }

        return applied;
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
                (configuration, var error) = await TryReadAsync<PrinterConfiguration>(async () => await printer.GetConfigurationAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                if (error is not null)
                {
                    PrintingLog.CapabilityReadFailed(_logger, channel.Id, channel.Endpoint, error);
                }
            }

            PrinterIdentity? identity = null;
            if (options.ReadIdentity)
            {
                (identity, var error) = await TryReadAsync(() => printer.GetIdentityAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
                if (error is not null)
                {
                    PrintingLog.IdentityReadFailed(_logger, channel.Id, channel.Endpoint, error);
                }
            }

            var sources = identity is null && configuration is null ? [] : SourcesOf(channel);
            return (Apply(channel, identity, configuration), sources);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The channel could not be opened or read. It stays as discovery found it.
            PrintingLog.EnrichmentOpenFailed(_logger, channel.Id, channel.Endpoint, exception);
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
    //
    // The failure is given back and not swallowed here: this method knows no printer, and a
    // log entry with no subject in it helps nobody. The caller owns the channel, so the
    // caller writes the entry. It also tells a timeout from a protocol error, because the
    // type of the exception is kept.
    private static async Task<(T? Value, Exception? Error)> TryReadAsync<T>(Func<Task<T?>> read, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return (await read().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private static IReadOnlyList<PrinterStatusSource> SourcesOf(DiscoveredPrinter channel)
    {
        if (channel.Endpoint.Scheme is PrinterScheme.Ipp or PrinterScheme.Ipps)
        {
            return [PrinterStatusSource.Ipp];
        }

        // A queue reports about itself, wherever the queue lives.
        if (channel.Endpoint.Scheme is PrinterScheme.Spooler or PrinterScheme.Cups)
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
        aliases.AddRange(identity.Aliases);

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
        //
        // The port is carried over from the channel, so a printer that answers the same
        // scheme on two ports keeps two identifiers rather than reporting one of them twice.
        var id = PrinterId.TryParseDeviceUuid(uuid, out var parsed)
            ? PrinterId.ForDeviceUuid(channel.Endpoint.Scheme, parsed, channel.Id.Port)
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

    private static List<PrinterDeviceKey> Distinct(List<PrinterDeviceKey> keys) => [.. keys.Distinct()];

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
        var channel = ChooseForPrint(device, id, payload.ContentType);
        var printer = _factory.Open(channel);
        try
        {
            var job = await printer.PrintAsync(payload, options, cancellationToken).ConfigureAwait(false);
            PrintingLog.JobSubmitted(_logger, job.JobId, id, channel.Endpoint, payload.ContentType, payload.Data.Length);

            // A job name and a user name are personal data, so they are at Debug and never
            // in an entry a reader may leave on in production.
            PrintingLog.JobNamed(_logger, job.JobId, id, job.JobName, options?.RequestingUserName);
            if (job.DroppedOptions.Count > 0 && _logger.IsEnabled(LogLevel.Warning))
            {
                PrintingLog.OptionsDropped(_logger, id, String.Join(", ", job.DroppedOptions), job.JobId);
            }

            return job;
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
    /// <remarks>
    /// Every channel of the device is tried, most preferred first, until one answers. A
    /// printer commonly advertises a channel it cannot actually serve — an IPPS port whose
    /// certificate no longer negotiates is the usual one — and the device is not
    /// unreachable while another of its channels still answers. Only when none does is the
    /// failure of the first reported, because that is the channel the caller asked for.
    /// </remarks>
    public async Task<PrinterStatus> GetStatusAsync(PrinterId id, CancellationToken cancellationToken)
    {
        var device = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        Exception? first = null;
        var failed = 0;
        foreach (var channel in StatusOrder(device, id))
        {
            var printer = _factory.Open(channel);
            try
            {
                var status = await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false);

                // Warning only when an earlier channel did not answer. A printer that
                // advertises a port it does not serve would otherwise raise one on every
                // call, which is what stops a log being safe to leave on.
                if (failed > 0)
                {
                    PrintingLog.StatusCameFromFallback(_logger, id, channel.Endpoint, failed);
                }

                return status;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Debug: when every channel fails, the first exception is thrown below and
                // carries the failure. These entries add the causes that it drops.
                PrintingLog.StatusChannelFailed(_logger, channel.Endpoint, id, exception);
                failed++;
                first ??= exception;
            }
            finally
            {
                if (printer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        throw first ?? NoAllowedTransport(device, id);
    }

    // The channels to ask for a status, most likely to answer first: the one the caller
    // named, then the rest in the configured transport order. ChooseForQueue picks the
    // head of this list, so a device with one working channel behaves as it always did.
    private List<DiscoveredPrinter> StatusOrder(PrinterDevice device, PrinterId id)
    {
        var allowed = Allowed(device);
        if (allowed.Count == 0)
        {
            throw NoAllowedTransport(device, id);
        }

        List<DiscoveredPrinter> ordered = [ChooseForQueue(device, id)];
        ordered.AddRange(allowed.Where(channel => channel != ordered[0]));
        return ordered;
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
        var allowed = Allowed(device);

        // A queue on a transport this manager may not open is no queue it can read, so the
        // check counts the allowed channels and not every channel of the device.
        if (!allowed.Exists(static channel => channel.HasJobQueue))
        {
            throw new NotSupportedException(
                $"Printer '{id}' has no job queue this manager may read, so a job sent to it cannot be watched. " +
                "A raw channel gives back no job identifier, and the job was reported complete when it was submitted. " +
                $"It may open {String.Join(", ", _options.Transports)}, and its endpoints are " +
                $"{String.Join(", ", device.Channels.Select(static channel => channel.Endpoint))}.");
        }

        // The job queue is addressed, not the device: an identity names no host, so the
        // endpoint of the chosen channel is what the queue can be opened from.
        var channel = ChooseForQueue(device, id);
        await foreach (var reading in _monitor.WatchJobAsync(PrinterId.FromEndpoint(channel.Endpoint), jobId, options, cancellationToken).ConfigureAwait(false))
        {
            yield return reading;
        }
    }

    // The channels of a device this manager may open, in the configured order. A transport
    // the list leaves out is not a candidate at all. The order ranks the channels that suit
    // a call equally; the payload rule below still decides which ones those are. Discovery
    // is untouched: an excluded channel is still found, still listed, and still tells the
    // device what it knows.
    private List<DiscoveredPrinter> Allowed(PrinterDevice device)
    {
        List<DiscoveredPrinter> allowed = [];
        foreach (var scheme in _options.Transports)
        {
            if (device.ChannelsByTransport.TryGetValue(scheme, out var channels))
            {
                allowed.AddRange(channels);
            }
        }

        return allowed;
    }

    private NotSupportedException NoAllowedTransport(PrinterDevice device, PrinterId id) =>
        new($"No channel of printer '{id}' is on a transport this manager may open. " +
            $"It may open {String.Join(", ", _options.Transports)}, and its endpoints are " +
            $"{String.Join(", ", device.Channels.Select(static channel => channel.Endpoint))}.");

    // The channel that prints the payload. The content type decides, because a printer
    // language is read by the device firmware and every other format is read by a driver.
    // A device identifier and a channel identifier cannot be told apart when no channel
    // reported an identity, so the identifier breaks a tie and never overrules the payload.
    private DiscoveredPrinter ChooseForPrint(PrinterDevice device, PrinterId id, string contentType)
    {
        var allowed = Allowed(device);
        if (allowed.Count == 0)
        {
            throw NoAllowedTransport(device, id);
        }

        // A channel that reported it does not read the content type is never chosen. A
        // channel that reported nothing has not refused, so it stays a candidate.
        List<DiscoveredPrinter> usable = [.. allowed.Where(channel => device.Accepts(channel, contentType) != false)];
        if (usable.Count == 0)
        {
            throw new NotSupportedException(
                $"No channel of printer '{id}' reads '{contentType}'. " +
                $"Its endpoints are {String.Join(", ", allowed.Select(static channel => channel.Endpoint))}.");
        }

        // A printer language such as ZPL is interpreted by the printer itself, so a
        // channel that rewrites the bytes prints the command source instead of the label.
        // Every other format prefers a channel with a job queue, so the job can be watched
        // after it is sent. Both fall back to the most preferred usable channel, which is
        // the best the device offers.
        var wantsPassthrough = IppDocumentFormat.IsRawLanguage(contentType, _formats);
        var named = usable.Find(channel => channel.Id == id);
        var chosen = named is not null && Fits(named, wantsPassthrough)
            ? named
            : usable.Find(channel => Fits(channel, wantsPassthrough)) ?? named ?? usable[0];

        PrintingLog.PrintChannelChosen(_logger, contentType, id, chosen.Endpoint, usable.Count, wantsPassthrough);

        // The fallback took a channel that does not do what the format needs. A label sent
        // this way prints its command source, and nothing else says so.
        if (!Fits(chosen, wantsPassthrough) && wantsPassthrough)
        {
            PrintingLog.PrintChannelGivesNoPassthrough(_logger, contentType, id, chosen.Endpoint);
        }

        return chosen;
    }

    private static bool Fits(DiscoveredPrinter channel, bool wantsPassthrough) =>
        wantsPassthrough ? channel.GivesPassthrough : channel.HasJobQueue;

    // The channel a queue is read from. Unlike a print, this needs a job queue whatever
    // the caller named, because a raw channel has no queue to read.
    private DiscoveredPrinter ChooseForQueue(PrinterDevice device, PrinterId id)
    {
        var allowed = Allowed(device);
        if (allowed.Count == 0)
        {
            throw NoAllowedTransport(device, id);
        }

        var named = allowed.Find(channel => channel.Id == id);
        var chosen = named?.HasJobQueue == true
            ? named
            : allowed.Find(static channel => channel.HasJobQueue) ?? named ?? allowed[0];

        // The fallback took a channel with no queue. A read of it reports no job at all.
        if (!chosen.HasJobQueue)
        {
            PrintingLog.QueueChannelHasNoQueue(_logger, id, chosen.Endpoint);
        }

        return chosen;
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
                PrintingLog.PrinterOpenedFromAddress(_logger, id);
                return device;
            }

            // A discovery is expensive, and a caller that sees this on every print holds an
            // identifier the manager cannot keep.
            PrintingLog.DiscoveryRanToResolve(_logger, id);
            _ = await DiscoverAsync(null, false, cancellationToken).ConfigureAwait(false);
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
