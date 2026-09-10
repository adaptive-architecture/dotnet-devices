using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing;

// Counts its calls, so a test can prove how often the manager ran a discovery. A null
// answer makes the source throw.
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

// Records what it was asked to print, so a test can prove the channel choice.
internal sealed class FakePrinterFactory : IPrinterFactory
{
    public List<DiscoveredPrinter> Opened { get; } = [];

    public List<PrinterId> OpenedById { get; } = [];

    // The answer for a network identifier the cache does not hold. Null makes the open fail,
    // the way an unreachable host would.
    public Func<PrinterId, DiscoveredPrinter> OpenById { get; set; }

    public IPrinter Open(DiscoveredPrinter printer)
    {
        Opened.Add(printer);
        return new FakePrinter(printer);
    }

    public Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken)
    {
        OpenedById.Add(id);
        return OpenById is null
            ? Task.FromException<IPrinter>(new InvalidOperationException($"No printer answers at '{id.Value}'."))
            : Task.FromResult<IPrinter>(new FakePrinter(OpenById(id)));
    }
}

internal sealed class FakePrinter : IPrinter
{
    private readonly DiscoveredPrinter _printer;

    public FakePrinter(DiscoveredPrinter printer) => _printer = printer;

    public PrinterId Id => _printer.Id;

    public PrinterEndpoint Endpoint => _printer.Endpoint;

    public PrinterInfo Info => _printer.Info;

    public Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(new PrintJobInfo("1", Id, PrintJobState.Queued));

    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterStatus(Id, PrinterStatusState.Idle));

    public Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterConfiguration(Id));
}

// Records what it was asked to watch and yields a scripted sequence, so a test can prove
// the manager delegated rather than invented the readings.
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
    public static DiscoveredPrinter Network(string host, int port, DiscoverySource source)
    {
        var id = PrinterId.FromNetwork(host);
        return new DiscoveredPrinter(id, new NetworkPrinterEndpoint(host, port), new PrinterInfo(id, host))
        {
            Source = source,
        };
    }

    public static DiscoveredPrinter Spooler(string queueName)
    {
        var id = PrinterId.FromSpooler(queueName);
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(queueName), new PrinterInfo(id, queueName))
        {
            Source = DiscoverySource.Spooler,
        };
    }
}
