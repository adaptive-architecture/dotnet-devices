using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing;

// Counts its calls. A null answer makes the source throw.
internal sealed class FakeMdnsDiscovery : IMdnsPrinterDiscovery
{
    private readonly IReadOnlyList<DiscoveredPrinter> _answer;

    public FakeMdnsDiscovery(IReadOnlyList<DiscoveredPrinter> answer) => _answer = answer;

    public int Calls { get; private set; }

    public Func<Task> Gate { get; set; }

    public async Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(MdnsPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        Calls++;
        if (Gate is not null)
        {
            await Gate().ConfigureAwait(false);
        }

        return _answer ?? throw new InvalidOperationException("The browse failed.");
    }
}

internal sealed class FakeSpoolerDiscovery : IPrinterDiscovery
{
    private readonly IReadOnlyList<DiscoveredPrinter> _answer;

    public FakeSpoolerDiscovery(IReadOnlyList<DiscoveredPrinter> answer) => _answer = answer;

    public int Calls { get; private set; }

    public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return _answer is null
            ? Task.FromException<IReadOnlyList<DiscoveredPrinter>>(new InvalidOperationException("The spooler failed."))
            : Task.FromResult(_answer);
    }
}

internal sealed class FakeNetworkProbe : INetworkPrinterDiscovery
{
    private readonly IReadOnlyList<DiscoveredPrinter> _answer;

    public FakeNetworkProbe(IReadOnlyList<DiscoveredPrinter> answer) => _answer = answer;

    public int Calls { get; private set; }

    public Task<IReadOnlyList<DiscoveredPrinter>> DiscoverAsync(NetworkPrinterDiscoveryOptions options, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(_answer);
    }
}

internal sealed class FakePrinterFactory : IPrinterFactory
{
    public List<DiscoveredPrinter> Opened { get; } = [];

    public List<PrinterId> OpenedById { get; } = [];

    // The answer for a network identifier the cache misses. Null makes the open fail.
    public Func<PrinterId, DiscoveredPrinter> OpenById { get; set; }

    // Makes an open fail the way a transport does when nothing answers at the address.
    public bool FailOpen { get; set; }

    // What each opened printer reports when an enrichment reads it.
    public Func<DiscoveredPrinter, PrinterConfiguration> ConfigurationOf { get; set; }

    public Func<DiscoveredPrinter, PrinterIdentity> IdentityOf { get; set; }

    public List<FakePrinter> Printers { get; } = [];

    public IPrinter Open(DiscoveredPrinter printer)
    {
        Opened.Add(printer);
        if (FailOpen)
        {
            throw new InvalidOperationException($"No printer answers at '{printer.Id.Authority}'.");
        }

        FakePrinter opened = new(printer)
        {
            Configuration = ConfigurationOf is null ? new PrinterConfiguration(printer.Id) : ConfigurationOf(printer),
            Identity = IdentityOf?.Invoke(printer),
        };
        Printers.Add(opened);
        return opened;
    }

    public Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)
    {
        OpenedById.Add(id);
        return OpenById is null
            ? Task.FromException<IPrinter>(new InvalidOperationException($"No printer answers at '{id.Authority}'."))
            : Task.FromResult<IPrinter>(new FakePrinter(OpenById(id)));
    }
}

internal sealed class FakePrinter : IPrinter
{
    private readonly DiscoveredPrinter _printer;

    public FakePrinter(DiscoveredPrinter printer) => _printer = printer;

    // What an enrichment read finds. Null makes the read throw, the way a printer that
    // does not answer does.
    public PrinterConfiguration Configuration { get; set; }

    public PrinterIdentity Identity { get; set; }

    public int ConfigurationCalls { get; private set; }

    public int IdentityCalls { get; private set; }

    public PrinterId Id => _printer.Id;

    public PrinterEndpoint Endpoint => _printer.Endpoint;

    public PrinterInfo Info => _printer.Info;

    public Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(new PrintJobInfo("1", Id, PrintJobState.Queued));

    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterStatus(Id, PrinterStatusState.Idle));

    public Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        ConfigurationCalls++;
        return Configuration is null
            ? Task.FromException<PrinterConfiguration>(new InvalidOperationException("The printer did not answer."))
            : Task.FromResult(Configuration);
    }

    public Task<PrinterIdentity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        IdentityCalls++;
        return Task.FromResult(Identity);
    }
}

// Yields a scripted sequence, so a test can prove the manager delegated the readings.
internal sealed class FakePrintJobMonitor : IPrintJobMonitor
{
    private readonly IReadOnlyList<PrintJobInfo> _readings;

    public FakePrintJobMonitor(IReadOnlyList<PrintJobInfo> readings) => _readings = readings;

    public PrinterId WatchedPrinterId { get; private set; }

    public string WatchedJobId { get; private set; }

    public async IAsyncEnumerable<PrintJobInfo> WatchJobAsync(
        PrinterId printerId, string jobId, PrintJobMonitorOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        WatchedPrinterId = printerId;
        WatchedJobId = jobId;
        foreach (var reading in _readings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return reading;
            await Task.Yield();
        }
    }
}

internal static class FakePrinters
{
    public static DiscoveredPrinter Raw(string host, DiscoverySource source, int port = NetworkPrinterEndpoint.DefaultPort)
    {
        var id = PrinterId.ForRaw(host, port);
        return new DiscoveredPrinter(id, NetworkPrinterEndpoint.Raw(host, port), new PrinterInfo(id, host))
        {
            Source = source,
        };
    }

    public static DiscoveredPrinter Ipp(string host, DiscoverySource source)
    {
        var id = PrinterId.ForIpp(host);
        return new DiscoveredPrinter(id, NetworkPrinterEndpoint.Ipp(host), new PrinterInfo(id, host))
        {
            Source = source,
        };
    }

    public static DiscoveredPrinter Identity(string host, Guid uuid, DiscoverySource source)
    {
        var id = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid);
        return new DiscoveredPrinter(id, NetworkPrinterEndpoint.Ipp(host), new PrinterInfo(id, host))
        {
            Source = source,
        };
    }

    public static DiscoveredPrinter Spooler(string queueName)
    {
        var id = PrinterId.ForSpooler(queueName);
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(queueName), new PrinterInfo(id, queueName))
        {
            Source = DiscoverySource.Spooler,
        };
    }

    // A queue that reported which device it prints to, the way a CUPS device URI does.
    public static DiscoveredPrinter Queue(string queueName, params PrinterDeviceKey[] aliases)
    {
        var id = PrinterId.ForSpooler(queueName);
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(queueName), new PrinterInfo(id, queueName))
        {
            Source = DiscoverySource.Spooler,
            Aliases = aliases,
        };
    }
}
