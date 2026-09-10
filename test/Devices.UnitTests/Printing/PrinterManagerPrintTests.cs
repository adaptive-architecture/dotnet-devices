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
        var printer = FakePrinters.Spooler("lobby");
        FakeMdnsDiscovery mdns = new([]);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([printer]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(1, mdns.Calls);
        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Fact]
    public async Task PrintAsync_NetworkMissOpensTheHostThroughTheFactoryAndDoesNotBrowse()
    {
        var id = PrinterId.FromNetwork("192.168.1.50");
        FakeMdnsDiscovery mdns = new([]);
        FakePrinterFactory factory = new() { OpenById = found => FakePrinters.Network(found.Value, 631, DiscoverySource.NetworkProbe) };
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(id, Zpl(), null, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(0, mdns.Calls);
        // The host was opened by identifier one time, then served from the cache.
        Assert.Equal(id, Assert.Single(factory.OpenedById));
        Assert.Equal(2, factory.Opened.Count);
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
            PrinterId.FromSpooler("ghost"), Zpl(), null, TestContext.Current.CancellationToken));

        Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_NetworkMiss_ReportsTheFactoryFailure()
    {
        FakeMdnsDiscovery mdns = new([]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrintAsync(
            PrinterId.FromNetwork("10.0.0.9"), Zpl(), null, TestContext.Current.CancellationToken));

        Assert.Contains("10.0.0.9", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_ConcurrentMissesCauseOneDiscovery()
    {
        var printer = FakePrinters.Spooler("lobby");
        TaskCompletionSource release = new();
        FakeMdnsDiscovery mdns = new([]) { Gate = () => release.Task };
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([printer]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        List<Task<PrintJobInfo>> prints = [];
        for (var i = 0; i < 10; i++)
        {
            prints.Add(manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken));
        }

        release.SetResult();
        _ = await Task.WhenAll(prints);

        // Ten callers missed together. One browse ran; the other nine waited for it.
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task GetStatusAsync_UsesTheCacheAndDoesNotDiscoverAgain()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        FakeMdnsDiscovery mdns = new([printer]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        var status = await manager.GetStatusAsync(printer.Id, TestContext.Current.CancellationToken);

        Assert.Equal(printer.Id, status.PrinterId);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task GetStatusAsync_ThrowsWhenTheIdentifierIsStillUnknownAfterARefresh()
    {
        FakeMdnsDiscovery mdns = new([]);
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetStatusAsync(
            PrinterId.FromSpooler("ghost"), TestContext.Current.CancellationToken));

        Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task GetStatusAsync_OpensThePrinterItWasAskedAbout()
    {
        var printer = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        _ = await manager.GetStatusAsync(printer.Id, TestContext.Current.CancellationToken);

        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
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
    // The Windows spooler sends RAW through. CUPS does not give the promise: submitting a
    // printer language as application/vnd.cups-raw stops the text filters, but a queue
    // with a driver, and a driverless queue, still convert the job for the device.
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

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        var job = await manager.PrintAsync(
            printer.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Fact]
    public async Task PrintAsync_OneIdentifierWithTwoEndpoints_PicksTheChannelTheJobNeeds()
    {
        // The same host advertises IPP and answers the raw probe: one identifier, two endpoints.
        var ipp = FakePrinters.Network("192.168.1.50", 631, DiscoverySource.Mdns);
        var raw = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.NetworkProbe);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([ipp]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([raw]), factory, NoMonitor());

        var found = await manager.DiscoverAsync(new PrinterManagerOptions { Probe = new() { Hosts = ["192.168.1.50"] } }, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(ipp.Id, Zpl(), null, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(ipp.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken);

        // A plain print takes the queue channel, a passthrough print the raw one.
        Assert.Equal(2, found.Count);
        Assert.Equal(631, Assert.IsType<NetworkPrinterEndpoint>(factory.Opened[0].Endpoint).Port);
        Assert.Equal(9100, Assert.IsType<NetworkPrinterEndpoint>(factory.Opened[1].Endpoint).Port);
    }

    [Fact]
    public async Task PrintAsync_PassthroughRefusesAnIppOnlyPrinter()
    {
        var printer = FakePrinters.Network("192.168.1.50", 631, DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => manager.PrintAsync(
            printer.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken));

        Assert.Contains("192.168.1.50", error.Message, StringComparison.Ordinal);
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

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        var watch = manager.WatchJobAsync(printer.Id, "1", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken);

        // An iterator body does not run until enumeration starts.
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

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
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

        var watch = manager.WatchJobAsync(PrinterId.FromSpooler("ghost"), "1", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var reading in watch)
            {
                _ = reading;
            }
        });

        Assert.Contains("ghost", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }
}
