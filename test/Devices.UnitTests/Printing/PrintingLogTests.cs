using AdaptArch.Devices.Printing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

// What a support engineer reads when a third party sends a log.
public class PrintingLogTests
{
    private const string Category = "AdaptArch.Devices.Printing";

    private static PrinterManager NewManager(
        FakeLoggerFactory factory,
        FakeMdnsDiscovery mdns = null,
        FakeSpoolerDiscovery spooler = null,
        FakeNetworkProbe probe = null,
        FakePrinterFactory printers = null) =>
        new(
            mdns ?? new FakeMdnsDiscovery([]),
            spooler ?? new FakeSpoolerDiscovery([]),
            probe ?? new FakeNetworkProbe([]),
            printers ?? new FakePrinterFactory(),
            new FakePrintJobMonitor([]),
            new PrinterManagerOptions { LoggerFactory = factory });

    [Fact]
    public async Task DiscoverAsync_ReportsWhatItFound()
    {
        FakeLoggerFactory factory = new();
        var manager = NewManager(factory, mdns: new FakeMdnsDiscovery([FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns)]));

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        var summary = Assert.Single(factory.WithId(2003));
        Assert.Equal(LogLevel.Information, summary.Level);
        Assert.Equal(Category, summary.Category);
        Assert.Contains("1 printers", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverAsync_OneSourceThatFailedIsAnError()
    {
        // The caller gets the printers of the other sources and never learns that one
        // source found nothing because it failed.
        FakeLoggerFactory factory = new();
        FakeMdnsDiscovery mdns = new([]) { Gate = () => throw new InvalidOperationException("the socket was refused") };
        var manager = NewManager(factory, mdns: mdns, spooler: new FakeSpoolerDiscovery([FakePrinters.Spooler("lobby")]));

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        var failure = Assert.Single(factory.WithId(2000));
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.Contains("Mdns", failure.Message, StringComparison.Ordinal);
        Assert.Equal("the socket was refused", failure.Exception.Message);
        Assert.Empty(factory.WithId(2001));
    }

    [Fact]
    public async Task DiscoverAsync_EverySourceThatFailedStaysAtDebug()
    {
        // The exception the caller gets carries each cause, so a second report would be
        // the same failure twice.
        FakeLoggerFactory factory = new();
        FakeMdnsDiscovery mdns = new([]) { Gate = () => throw new InvalidOperationException("no interface") };
        // A null answer is how this fake reports a spooler that failed.
        var manager = NewManager(factory, mdns: mdns, spooler: new FakeSpoolerDiscovery(null));

        _ = await Assert.ThrowsAsync<PrinterDiscoveryException>(
            () => manager.DiscoverAsync(null, TestContext.Current.CancellationToken));

        Assert.Empty(factory.WithId(2000));
        Assert.Equal(2, factory.WithId(2001).Count);
        Assert.All(factory.WithId(2001), entry => Assert.Equal(LogLevel.Debug, entry.Level));
    }

    [Fact]
    public async Task GetStatusAsync_AFallbackChannelIsAWarningAndTheFailureIsDebug()
    {
        // A printer that advertises a port it does not serve must not raise a warning on
        // every call, so the per-channel failure stays at Debug.
        FakeLoggerFactory factory = new();
        var ipp = FakePrinters.Ipp("printer.local", DiscoverySource.Mdns);
        var raw = FakePrinters.Raw("printer.local", DiscoverySource.Mdns);
        FakePrinterFactory printers = new() { FailStatusOn = channel => channel.Id == ipp.Id };
        var manager = NewManager(factory, mdns: new FakeMdnsDiscovery([ipp, raw]), printers: printers);
        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        _ = await manager.GetStatusAsync(devices[0].Channels[0].Id, TestContext.Current.CancellationToken);

        Assert.All(factory.WithId(2030), entry => Assert.Equal(LogLevel.Debug, entry.Level));
        var fallback = Assert.Single(factory.WithId(2031));
        Assert.Equal(LogLevel.Warning, fallback.Level);
    }

    [Fact]
    public void EveryEntryNamesItsSubject()
    {
        // A line pasted into an issue must be readable by itself, so each template carries
        // a printer, a job or a host. A summary carries a count instead.
        FakeLoggerFactory factory = new();
        var logger = factory.CreateLogger(Category);

        PrintingLogProbe.WriteOne(logger);

        var entry = Assert.Single(factory.Entries);
        Assert.Contains("raw://printer.local", entry.Message, StringComparison.Ordinal);
    }
}

// Reaches one generated method, so the test above reads a real template and not a copy.
internal static class PrintingLogProbe
{
    public static void WriteOne(ILogger logger) =>
        PrintingLog.DiscoveryRanToResolve(logger, PrinterId.ForRaw("printer.local"));
}
