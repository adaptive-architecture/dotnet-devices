using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterManagerPrintTests
{
    private static PrinterPayload Zpl() => PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

    // Printing tests never watch, so an empty script satisfies the constructor.
    private static FakePrintJobMonitor NoMonitor() => new([]);

    private static readonly Guid Uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");

    [Fact]
    public async Task PrintAsync_DiscoversOnceWhenTheCacheMissesAnIdentity()
    {
        var printer = FakePrinters.Identity("192.168.1.50", Uuid, DiscoverySource.Mdns);
        FakeMdnsDiscovery mdns = new([printer]);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(printer.Id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(1, mdns.Calls);
        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Theory]
    [InlineData("raw://192.168.1.50")]
    [InlineData("ipp://192.168.1.50")]
    [InlineData("spooler://lobby")]
    public async Task PrintAsync_AnAddressFormOpensDirectlyAndDoesNotBrowse(string text)
    {
        // The scheme states the transport and the authority states the address, so there
        // is nothing left to discover or to probe.
        var id = PrinterId.Parse(text);
        FakeMdnsDiscovery mdns = new([]);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var job = await manager.PrintAsync(id, Zpl(), null, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(id, Zpl(), null, TestContext.Current.CancellationToken);

        Assert.Equal("1", job.JobId);
        Assert.Equal(0, mdns.Calls);
        Assert.Empty(factory.OpenedById);
        Assert.Equal(2, factory.Opened.Count);
        Assert.Equal(id, factory.Opened[0].Id);
    }

    [Fact]
    public async Task PrintAsync_UsesTheCacheAndDoesNotDiscoverAgain()
    {
        var printer = FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns);
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

        var ghost = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Uuid);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrintAsync(
            ghost, Zpl(), null, TestContext.Current.CancellationToken));

        Assert.Contains(Uuid.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_AnAddressFormReachesTheTransportAndReportsItsFailure()
    {
        // A host that answers nothing is no longer an addressing problem the manager can
        // report: the identifier names the channel, so the transport error is the answer.
        FakeMdnsDiscovery mdns = new([]);
        FakePrinterFactory factory = new() { FailOpen = true };
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrintAsync(
            PrinterId.ForRaw("10.0.0.9"), Zpl(), null, TestContext.Current.CancellationToken));

        Assert.Contains("10.0.0.9", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, mdns.Calls);
    }

    [Fact]
    public async Task PrintAsync_ConcurrentMissesCauseOneDiscovery()
    {
        var printer = FakePrinters.Identity("192.168.1.50", Uuid, DiscoverySource.Mdns);
        TaskCompletionSource release = new();
        FakeMdnsDiscovery mdns = new([printer]) { Gate = () => release.Task };
        PrinterManager manager = new(mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), NoMonitor());

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
        var printer = FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns);
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

        var ghost = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Uuid);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetStatusAsync(
            ghost, TestContext.Current.CancellationToken));

        Assert.Contains(Uuid.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }

    [Fact]
    public async Task GetStatusAsync_OpensThePrinterItWasAskedAbout()
    {
        var printer = FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        _ = await manager.GetStatusAsync(printer.Id, TestContext.Current.CancellationToken);

        Assert.Equal(printer.Id, Assert.Single(factory.Opened).Id);
    }

    [Fact]
    public async Task PrintAsync_PassthroughPrintsToARawNetworkChannel()
    {
        var printer = FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns);
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
    public async Task DiscoverAsync_OneHostOnTwoChannels_IsOneDeviceWithTwoChannels()
    {
        // The same host advertises IPP and answers the raw probe. That is one printer
        // reachable two ways, not two printers.
        var ipp = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        var raw = FakePrinters.Raw("192.168.1.50", DiscoverySource.NetworkProbe);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([ipp]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([raw]), new FakePrinterFactory(), NoMonitor());

        var found = await manager.DiscoverAsync(new PrinterManagerOptions { Probe = new() { Hosts = ["192.168.1.50"] } }, TestContext.Current.CancellationToken);

        var device = Assert.Single(found);
        Assert.Equal(2, device.Channels.Count);
        Assert.NotEqual(ipp.Id, raw.Id);
        Assert.Equal(ipp.Id.DeviceKey, device.Key);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Ipp]);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Raw]);
        Assert.True(device.HasJobQueue);
        Assert.True(device.GivesPassthrough);
    }

    [Fact]
    public async Task PrintAsync_PicksTheChannelTheJobNeedsFromTheWholeDevice()
    {
        var ipp = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        var raw = FakePrinters.Raw("192.168.1.50", DiscoverySource.NetworkProbe);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([ipp]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([raw]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(new PrinterManagerOptions { Probe = new() { Hosts = ["192.168.1.50"] } }, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(ipp.Id, Zpl(), null, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(ipp.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken);

        // A plain print takes the queue channel. A passthrough print crosses to the raw
        // channel of the same device, even though the caller named the IPP one.
        Assert.Equal(631, Assert.IsType<NetworkPrinterEndpoint>(factory.Opened[0].Endpoint).Port);
        Assert.Equal(9100, Assert.IsType<NetworkPrinterEndpoint>(factory.Opened[1].Endpoint).Port);
    }

    [Fact]
    public async Task PrintAsync_CrossesFromASpoolerQueueToTheRawChannelOfTheSameDevice()
    {
        // The queue vouched for the host through its device URI, so the two are one
        // device and a passthrough job can take the channel that keeps its bytes.
        var raw = FakePrinters.Raw("192.168.1.50", DiscoverySource.NetworkProbe);
        var queue = FakePrinters.Queue("EPSON_L6270", PrinterDeviceKey.ForHost("192.168.1.50"));
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([]), new FakeSpoolerDiscovery([queue]), new FakeNetworkProbe([raw]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(new PrinterManagerOptions { Probe = new() { Hosts = ["192.168.1.50"] } }, TestContext.Current.CancellationToken);
        _ = await manager.PrintAsync(queue.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken);

        Assert.Equal(PrinterScheme.Raw, Assert.Single(factory.Opened).Endpoint.Scheme);
    }

    [Fact]
    public async Task PrintAsync_PassthroughRefusesAnIppOnlyPrinter()
    {
        var printer = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        FakePrinterFactory factory = new();
        PrinterManager manager = new(
            new FakeMdnsDiscovery([printer]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, NoMonitor());

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => manager.PrintAsync(
            printer.Id, Zpl(), new PrintOptions { RequirePassthrough = true }, TestContext.Current.CancellationToken));

        Assert.Contains("192.168.1.50", error.Message, StringComparison.Ordinal);
        Assert.Empty(factory.Opened);
    }

    [Fact]
    public async Task WatchJobAsync_RefusesARawNetworkChannel()
    {
        var printer = FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns);
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
        var printer = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
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

        var ghost = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Uuid);
        var watch = manager.WatchJobAsync(ghost, "1", new PrintJobMonitorOptions(), TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var reading in watch)
            {
                _ = reading;
            }
        });

        Assert.Contains(Uuid.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Equal(1, mdns.Calls);
    }
}
