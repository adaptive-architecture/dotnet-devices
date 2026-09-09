using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class MdnsPrinterDiscoveryTests
{
    private static readonly TimeSpan ShortBrowse = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task DiscoverPrintersAsync_ReadsNameLocationAndDriverFromTxtRecord()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            instance: "Office Printer",
            serviceType: MdnsPrinterDiscoveryOptions.IppServiceType,
            target: "printer.local",
            port: 631,
            address: "192.168.1.50",
            texts: ["ty=HP LaserJet 400", "note=Second floor", "pdl=application/postscript"]));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal("192.168.1.50", printer.Id.Value);
        Assert.Equal(PrinterIdKind.Network, printer.Id.Kind);
        Assert.Equal("HP LaserJet 400", printer.Info.Name);
        Assert.Equal("Second floor", printer.Info.Location);
        Assert.Equal("application/postscript", printer.Info.DriverName);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint);
        Assert.Equal(631, endpoint.Port);
        Assert.Equal("192.168.1.50", endpoint.Host);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_WithoutModelAttribute_UsesInstanceName()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            instance: "Front Desk",
            serviceType: MdnsPrinterDiscoveryOptions.IppServiceType,
            target: "desk.local",
            port: 631,
            address: "192.168.1.51",
            texts: []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("Front Desk", Assert.Single(printers).Info.Name);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_WithoutAddressRecord_UsesServiceTargetName()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            instance: "No Address",
            serviceType: MdnsPrinterDiscoveryOptions.IppServiceType,
            target: "printer-3.local",
            port: 631,
            address: null,
            texts: []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("printer-3.local", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_SameInstanceOnTwoInterfaces_ReturnsOnePrinter()
    {
        var answer = MdnsResponses.Printer(
            instance: "Shared",
            serviceType: MdnsPrinterDiscoveryOptions.IppServiceType,
            target: "shared.local",
            port: 631,
            address: "192.168.1.52",
            texts: ["ty=Shared Printer"]);
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(
            new FakeUdpChannel(answer),
            new FakeUdpChannel(answer)));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("Shared Printer", Assert.Single(printers).Info.Name);
    }

    // The Bonjour Printing Specification requires one service name for every protocol a
    // printer supports. The browse must therefore report one printer, and it must choose
    // the raw channel, because that is the only endpoint TcpPrinterTransport can print to.
    [Fact]
    public async Task DiscoverPrintersAsync_InstanceOnAllServiceTypes_ReturnsOnePrinterOnRawPort()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.IppServiceType, "multi.local", 631, "192.168.1.53", ["ty=Multi"]),
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.LpdServiceType, "multi.local", 515, "192.168.1.53", ["ty=Multi"]),
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.PdlDatastreamServiceType, "multi.local", 9100, "192.168.1.53", ["ty=Multi"]));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint).Port);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_LpdOnlyPrinter_KeepsLpdPort()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            "Old", MdnsPrinterDiscoveryOptions.LpdServiceType, "old.local", 515, "192.168.1.54", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(515, Assert.IsType<NetworkPrinterEndpoint>(Assert.Single(printers).Endpoint).Port);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_ResultsAreSortedByIdentifier()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.Printer("B", MdnsPrinterDiscoveryOptions.IppServiceType, "b.local", 631, "192.168.1.70", []),
            MdnsResponses.Printer("A", MdnsPrinterDiscoveryOptions.IppServiceType, "a.local", 631, "192.168.1.60", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(["192.168.1.60", "192.168.1.70"], printers.Select(printer => printer.Id.Value));
    }

    [Fact]
    public async Task DiscoverPrintersAsync_PointerWithoutServiceRecord_IsSkipped()
    {
        FakeUdpChannel channel = new(MdnsResponses.PointerOnly(
            "Ghost", MdnsPrinterDiscoveryOptions.IppServiceType));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_MalformedAnswer_IsIgnored()
    {
        FakeUdpChannel channel = new(
            [0, 0, 0],
            MdnsResponses.Printer("Good", MdnsPrinterDiscoveryOptions.IppServiceType, "good.local", 631, "192.168.1.55", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("192.168.1.55", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_NoAnswers_ReturnsEmptyWhenWindowEnds()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_SendsOneQueryPerServiceTypeAndRound()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));
        var options = NewOptions();
        options.ServiceTypes = [MdnsPrinterDiscoveryOptions.IppServiceType, MdnsPrinterDiscoveryOptions.LpdServiceType];
        options.QueryRetries = 0;

        _ = await discovery.DiscoverPrintersAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(2, channel.Sent.Count);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_DisposesEveryChannel()
    {
        FakeUdpChannel first = new();
        FakeUdpChannel second = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(first, second));

        _ = await discovery.DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_CallerCancellation_Propagates()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));
        var options = NewOptions();
        options.BrowseTimeout = TimeSpan.FromMinutes(1);
        using CancellationTokenSource callerSource = new();
        await callerSource.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverPrintersAsync(options, callerSource.Token));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_NoUsableInterface_ReturnsEmpty()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());

        var printers = await discovery
            .DiscoverPrintersAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_EmptyServiceTypes_ReturnsEmpty()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(new FakeUdpChannel()));
        var options = NewOptions();
        options.ServiceTypes = [];

        var printers = await discovery
            .DiscoverPrintersAsync(options, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverPrintersAsync_NullOptions_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => discovery.DiscoverPrintersAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverPrintersAsync_NonPositiveBrowseTimeout_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());
        MdnsPrinterDiscoveryOptions options = new() { BrowseTimeout = TimeSpan.Zero };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverPrintersAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverPrintersAsync_NegativeRetries_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());
        MdnsPrinterDiscoveryOptions options = new() { QueryRetries = -1 };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverPrintersAsync(options, TestContext.Current.CancellationToken));
    }

    private static MdnsPrinterDiscoveryOptions NewOptions() => new() { BrowseTimeout = ShortBrowse };
}
