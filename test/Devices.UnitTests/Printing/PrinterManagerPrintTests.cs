using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterManagerPrintTests
{
    private static PrinterPayload Zpl() => PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

    // Printing tests never watch, so an empty script satisfies the constructor.
    private static FakePrintJobMonitor NoMonitor() => new([]);

    [Fact]
    public async Task PrintAsync_DiscoversOnceWhenTheCacheIsEmpty()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        FakeMdnsDiscovery mdns = new([printer]);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(1, mdns.Calls);
        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Fact]
    public async Task PrintAsync_UsesTheCacheAndDoesNotDiscoverAgain()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        FakeMdnsDiscovery mdns = new([printer]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_ThrowsWhenTheIdentifierIsStillUnknownAfterARefresh()
    {
        FakeMdnsDiscovery mdns = new([]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrintAsync(
            PrinterId.FromNetwork("10.0.0.9"), Zpl(), null, TestContext.Current.CancellationToken));

        Assert.Contains("10.0.0.9", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_ConcurrentMissesCauseOneDiscovery()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        TaskCompletionSource release = new();
        FakeMdnsDiscovery mdns = new([printer]) { Gate = () => release.Task };
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        List<Task<PrintJobInfo>> prints = [];
        for (var i = 0; i < 10; i += 1)
        {
            prints.Add(manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken));
        }

        release.SetResult();
        _ = await Task.WhenAll(prints);

        // Ten callers missed together. One browse ran; the other nine waited for it.
        Assert.Equal(1, mdns.Calls);
    }

    [Theory]
    // A raw network channel always sends the bytes through unchanged.
    [InlineData(9100, false, true)]
    [InlineData(9100, true, true)]
    // Port 631 is the IPP channel, which may filter or rasterise.
    [InlineData(631, false, false)]
    [InlineData(631, true, false)]
    public void GivesPassthrough_ReadsTheNetworkPort(int port, bool isWindows, bool expected) =>
        Assert.Equal(expected, PrinterManager.GivesPassthrough(new NetworkPrinterEndpoint("printer.local", port), isWindows));

    [Theory]
    // The Windows spooler sends RAW through; CUPS applies its own filter chain.
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GivesPassthrough_ReadsThePlatformForASpoolerQueue(bool isWindows, bool expected) =>
        Assert.Equal(expected, PrinterManager.GivesPassthrough(new SpoolerPrinterEndpoint("lobby"), isWindows));

    [Fact]
    public void GivesPassthrough_IsFalseForUsb() =>
        Assert.False(PrinterManager.GivesPassthrough(new UsbPrinterEndpoint(0x04B8, 0x0202), true));

    [Fact]
    public async Task PrintAsync_PassthroughPrintsToARawNetworkChannel()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(
            printer.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Fact]
    public async Task PrintAsync_PassthroughRefusesAnIppOnlyPrinter()
    {
        var printer = FakePrinters.Network("192.168.1.50", 631, DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => manager.PrintAsync(
            printer.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken));

        Assert.Contains("192.168.1.50", error.Message, StringComparison.Ordinal);
        // It refused before it opened anything.
        Assert.Empty(factory.Opened);
    }

    [Theory]
    // The third port pins the rule to "631 only" rather than "not 9100".
    [InlineData(631, true)]
    [InlineData(9100, false)]
    [InlineData(515, false)]
    public void HasJobQueue_ReadsTheNetworkPort(int port, bool expected) =>
        Assert.Equal(expected, PrinterManager.HasJobQueue(new NetworkPrinterEndpoint("printer.local", port)));

    [Fact]
    public void HasJobQueue_IsTrueForASpoolerQueue() =>
        Assert.True(PrinterManager.HasJobQueue(new SpoolerPrinterEndpoint("lobby")));

    [Fact]
    public void HasJobQueue_IsFalseForUsb() =>
        Assert.False(PrinterManager.HasJobQueue(new UsbPrinterEndpoint(0x04B8, 0x0202)));

    [Fact]
    public async Task WatchJobAsync_RefusesARawNetworkChannel()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        var watch = manager.WatchJobAsync(printer.Id, "1", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken);

        // An async iterator body does not run until enumeration starts, so this must enumerate.
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var reading in watch)
            {
                _ = reading;
            }
        });

        Assert.Contains("192.168.1.50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WatchJobAsync_DelegatesToTheMonitorForAnIppPrinter()
    {
        var printer = FakePrinters.Network("192.168.1.50", 631, DiscoverySource.Mdns);
        var readings = new List<PrintJobInfo>
        {
            new("42", printer.Id, PrintJobState.Printing),
            new("42", printer.Id, PrintJobState.Completed),
        };
        FakePrintJobMonitor monitor = new(readings);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), monitor);

        List<PrintJobInfo> received = [];
        await foreach (var reading in manager.WatchJobAsync(printer.Id, "42", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken))
        {
            received.Add(reading);
        }

        Assert.Equal(printer.Id, monitor.WatchedPrinterId);
        Assert.Equal("42", monitor.WatchedJobId);
        Assert.Equal(readings, received);
    }

    [Fact]
    public async Task WatchJobAsync_ThrowsWhenTheIdentifierIsStillUnknownAfterARefresh()
    {
        FakeMdnsDiscovery mdns = new([]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        var watch = manager.WatchJobAsync(PrinterId.FromNetwork("10.0.0.9"), "1", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var reading in watch)
            {
                _ = reading;
            }
        });

        Assert.Contains("10.0.0.9", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }
}
