using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterManagerDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsEveryEntryFromEverySource()
    {
        FakeMdnsDiscovery mdns = new([FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns)]);
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        FakeNetworkProbe probe = new([]);
        PrinterManager manager = new(mdns, spooler, probe, new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(2, printers.Count);
        Assert.Contains(printers, p => p.Source == DiscoverySource.Mdns);
        Assert.Contains(printers, p => p.Source == DiscoverySource.Spooler);
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotRunTheProbeWithoutOptions()
    {
        FakeNetworkProbe probe = new([]);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([]), new FakeSpoolerDiscovery([]), probe, new FakePrinterFactory(), new FakePrintJobMonitor([]));

        _ = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(0, probe.Calls);
    }

    [Fact]
    public async Task DiscoverAsync_RunsTheProbeWhenOptionsCarryIt()
    {
        FakeNetworkProbe probe = new([FakePrinters.Network("10.0.0.7", 9100, DiscoverySource.NetworkProbe)]);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([]), new FakeSpoolerDiscovery([]), probe, new FakePrinterFactory(), new FakePrintJobMonitor([]));
        PrinterManagerOptions options = new()
        {
            Probe = new NetworkPrinterDiscoveryOptions { Hosts = ["10.0.0.7"] },
        };

        var printers = await manager.DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(1, probe.Calls);
        Assert.Equal(DiscoverySource.NetworkProbe, Assert.Single(printers).Source);
    }

    [Fact]
    public async Task DiscoverAsync_OneFailingSourceDoesNotHideTheOthers()
    {
        // A null answer makes the fake throw.
        FakeMdnsDiscovery mdns = new(null);
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        PrinterManager manager = new(mdns, spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("lobby", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_ThrowsWhenEverySourceFails()
    {
        PrinterManager manager = new(
            new FakeMdnsDiscovery(null), new FakeSpoolerDiscovery(null), new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var error = await Assert.ThrowsAsync<PrinterDiscoveryException>(
            () => manager.DiscoverAsync(null, TestContext.Current.CancellationToken));

        Assert.Equal(2, error.Failures.Count);
        Assert.Equal("The browse failed.", error.Failures[DiscoverySource.Mdns].Message);
        Assert.Equal("The spooler failed.", error.Failures[DiscoverySource.Spooler].Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsOneEndpointOneTime()
    {
        // The browse and the probe both report the raw channel of one host.
        var fromBrowse = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns);
        var fromProbe = FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.NetworkProbe);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([fromBrowse]), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([fromProbe]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(new PrinterManagerOptions { Probe = new() { Hosts = ["192.168.1.50"] } }, TestContext.Current.CancellationToken);

        _ = Assert.Single(printers);
    }

    [Fact]
    public async Task DiscoverAsync_DoesNotThrowWhenOneSourceFailsAndAnotherSucceedsEmpty()
    {
        // A quiet link finding nothing is an answer, not a failure.
        FakeMdnsDiscovery mdns = new([]);
        FakeSpoolerDiscovery spooler = new(null);
        PrinterManager manager = new(mdns, spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverAsync_ThrowsOperationCanceledWhenTheCallerTokenIsCancelled()
    {
        FakeMdnsDiscovery mdns = new([]) { Gate = () => throw new OperationCanceledException() };
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        PrinterManager manager = new(mdns, spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));
        using CancellationTokenSource cancelledSource = new();
        cancelledSource.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.DiscoverAsync(null, cancelledSource.Token));
    }

    [Fact]
    public async Task DiscoverAsync_TreatsACancellationFromASourceAsThatSourceFailingWhenTheCallerDidNotCancel()
    {
        // An HttpClient timeout arrives uncancelled, and must not hide the other sources.
        FakeMdnsDiscovery mdns = new([]) { Gate = () => throw new OperationCanceledException() };
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        PrinterManager manager = new(mdns, spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("lobby", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsTheSpoolerWhenTheOptionsSaySo()
    {
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        PrinterManager manager = new(
            new FakeMdnsDiscovery([]), spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(
            new PrinterManagerOptions { IncludeSpooler = false }, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
        Assert.Equal(0, spooler.Calls);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsTheBrowseWhenTheOptionsSaySo()
    {
        FakeMdnsDiscovery mdns = new([FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns)]);
        PrinterManager manager = new(
            mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(
            new PrinterManagerOptions { IncludeMdns = false }, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
        Assert.Equal(0, mdns.Calls);
    }

    [Fact]
    public async Task DiscoverAsync_RunsTheBrowseByDefault()
    {
        FakeMdnsDiscovery mdns = new([FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns)]);
        PrinterManager manager = new(
            mdns, new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));

        var printers = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(DiscoverySource.Mdns, Assert.Single(printers).Source);
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsEmptyAndDoesNotThrowWhenEverySourceIsSwitchedOff()
    {
        FakeMdnsDiscovery mdns = new([FakePrinters.Network("192.168.1.50", 9100, DiscoverySource.Mdns)]);
        FakeSpoolerDiscovery spooler = new([FakePrinters.Spooler("lobby")]);
        PrinterManager manager = new(mdns, spooler, new FakeNetworkProbe([]), new FakePrinterFactory(), new FakePrintJobMonitor([]));
        PrinterManagerOptions options = new()
        {
            IncludeMdns = false,
            IncludeSpooler = false,
            Probe = null,
        };

        var printers = await manager.DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
        Assert.Equal(0, mdns.Calls);
        Assert.Equal(0, spooler.Calls);
    }
}
