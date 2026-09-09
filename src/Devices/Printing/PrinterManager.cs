using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Default <see cref="IPrinterManager"/>. Runs every configured discovery source
/// together and combines the printers they found.
/// </summary>
public sealed class PrinterManager : IPrinterManager
{
    private readonly IMdnsPrinterDiscovery _mdns;
    private readonly IPrinterDiscovery _spooler;
    private readonly INetworkPrinterDiscovery _probe;
    private readonly IPrinterFactory _factory;
    private readonly IPrintJobMonitor _monitor;
    private readonly ConcurrentDictionary<PrinterId, DiscoveredPrinter> _cache = new();
    private readonly SemaphoreSlim _refresh = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterManager"/> class.
    /// </summary>
    /// <param name="mdns">The multicast DNS discovery source.</param>
    /// <param name="spooler">The operating system print spooler discovery source.</param>
    /// <param name="probe">The direct network probe discovery source.</param>
    /// <param name="factory">The factory used to open a printer once found.</param>
    /// <param name="monitor">The monitor used to watch a job on a printer that has a job queue.</param>
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
    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(PrinterManagerOptions? options, CancellationToken cancellationToken)
    {
        var effective = options ?? new PrinterManagerOptions();
        List<Task<(IReadOnlyList<DiscoveredPrinter> Printers, Exception? Error)>> running = [];
        if (effective.IncludeMdns)
        {
            running.Add(RunAsync(() => _mdns.DiscoverPrintersAsync(effective.Mdns, cancellationToken), cancellationToken));
        }

        if (effective.IncludeSpooler)
        {
            running.Add(RunAsync(() => _spooler.GetPrintersAsync(cancellationToken), cancellationToken));
        }

        if (effective.Probe is not null)
        {
            running.Add(RunAsync(() => _probe.DiscoverNetworkPrintersAsync(effective.Probe, cancellationToken), cancellationToken));
        }

        var results = await Task.WhenAll(running).ConfigureAwait(false);

        List<DiscoveredPrinter> found = [];
        Exception? lastFailure = null;
        var succeeded = 0;
        foreach (var result in results)
        {
            if (result.Error is null)
            {
                succeeded++;
                found.AddRange(result.Printers);
            }
            else
            {
                lastFailure = result.Error;
            }
        }

        // Count successes, not results: a source that answered with an empty list has
        // answered. An empty local link is a normal result, not a failure.
        if (succeeded == 0 && lastFailure is not null)
        {
            throw lastFailure;
        }

        foreach (var printer in found)
        {
            _cache[printer.Id] = printer;
        }

        return found;
    }

    /// <inheritdoc />
    public async Task<PrintJobInfo> PrintAsync(PrinterId id, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var entry = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        if (options?.RequirePassthrough == true && !GivesPassthrough(entry.Endpoint, OperatingSystem.IsWindows()))
        {
            throw new NotSupportedException(
                $"Printer '{entry.Id}' has no channel that sends the payload unchanged, and the options require one. " +
                $"Its endpoint is {entry.Endpoint}.");
        }

        var printer = _factory.Open(entry);
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
        var entry = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);

        var printer = _factory.Open(entry);
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

    // IPP, IPPS and CUPS can filter or rasterise a document; a raw channel and the Windows
    // spooler cannot. The platform is a parameter so a test on Linux can prove both branches.
    internal static bool GivesPassthrough(PrinterEndpoint endpoint, bool isWindows)
    {
        if (endpoint is NetworkPrinterEndpoint network)
        {
            // The factory makes a RawPrinter for exactly these ports.
            return network.Port != IppPrinterStatusClient.DefaultPort;
        }

        if (endpoint is SpoolerPrinterEndpoint)
        {
            return isWindows;
        }

        return false;
    }

    // A raw channel gives back no job identifier and has no queue to poll. Unlike
    // GivesPassthrough this needs no platform: both spooler drivers expose a job queue.
    internal static bool HasJobQueue(PrinterEndpoint endpoint)
    {
        if (endpoint is NetworkPrinterEndpoint network)
        {
            return network.Port == IppPrinterStatusClient.DefaultPort;
        }

        return endpoint is SpoolerPrinterEndpoint;
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

    // Guards must throw as soon as WatchJobAsync is called, not on first enumeration. An
    // iterator body only starts running when the caller enumerates it, so the guards live in
    // the public method above and this private iterator holds the rest of the work.
    private async IAsyncEnumerable<PrintJobInfo> WatchJobAsyncCore(
        PrinterId id,
        string jobId,
        PrintJobMonitorOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var entry = await ResolveAsync(id, cancellationToken).ConfigureAwait(false);
        if (!HasJobQueue(entry.Endpoint))
        {
            throw new NotSupportedException(
                $"Printer '{entry.Id}' has no job queue, so a job sent to it cannot be watched. " +
                "A raw channel gives back no job identifier, and the job was reported complete when it was submitted. " +
                $"Its endpoint is {entry.Endpoint}.");
        }

        await foreach (var reading in _monitor.WatchJobAsync(id, jobId, options, cancellationToken).ConfigureAwait(false))
        {
            yield return reading;
        }
    }

    // A failed print does NOT refresh: most print failures are not addressing problems.
    private async Task<DiscoveredPrinter> ResolveAsync(PrinterId id, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        await _refresh.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have filled the cache while this one waited. Without this
            // second look the semaphore serialises the browses instead of preventing them.
            if (_cache.TryGetValue(id, out cached))
            {
                return cached;
            }

            _ = await DiscoverAsync(null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _refresh.Release();
        }

        return _cache.TryGetValue(id, out var found)
            ? found
            : throw new InvalidOperationException($"No printer with the identifier '{id}' was found, and a fresh discovery did not find one either.");
    }

    // The failure travels in the result, not a field, so one call cannot see another's error.
    // Only the caller's own cancellation fails the whole call: CupsSpoolerDriver reaches CUPS
    // over HttpClient, whose timeout surfaces as TaskCanceledException, and a hung daemon must
    // not hide the printers the other sources found.
    private static async Task<(IReadOnlyList<DiscoveredPrinter> Printers, Exception? Error)> RunAsync(
        Func<Task<IReadOnlyList<DiscoveredPrinter>>> source, CancellationToken cancellationToken)
    {
        try
        {
            return (await source().ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ([], exception);
        }
    }
}
