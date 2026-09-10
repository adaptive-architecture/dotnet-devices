using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class MdnsPrinterDiscoveryTests
{
    private static readonly TimeSpan ShortBrowse = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task DiscoverAsync_ReadsNameLocationAndDriverFromTxtRecord()
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
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal("192.168.1.50", printer.Id.Value);
        Assert.Equal(PrinterIdKind.Network, printer.Id.Kind);
        Assert.Equal("HP LaserJet 400", printer.Info.Name);
        Assert.Equal("Second floor", printer.Info.Location);
        Assert.Equal("application/postscript", printer.Info.DriverName);
        Assert.Equal(DiscoverySource.Mdns, printer.Source);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint);
        Assert.Equal(631, endpoint.Port);
        Assert.Equal("192.168.1.50", endpoint.Host);
    }

    [Fact]
    public async Task DiscoverAsync_WithoutModelAttribute_UsesInstanceName()
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
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("Front Desk", Assert.Single(printers).Info.Name);
    }

    [Fact]
    public async Task DiscoverAsync_WithoutAddressRecord_UsesServiceTargetName()
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
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("printer-3.local", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_SameInstanceOnTwoInterfaces_ReturnsOnePrinter()
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
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("Shared Printer", Assert.Single(printers).Info.Name);
    }

    // One service name covers every protocol, so the browse reports one printer, on the
    // raw channel: the only endpoint TcpPrinterTransport can print to.
    [Fact]
    public async Task DiscoverAsync_InstanceOnAllServiceTypes_ReturnsOnePrinterOnRawPort()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.IppServiceType, "multi.local", 631, "192.168.1.53", ["ty=Multi"]),
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.LpdServiceType, "multi.local", 515, "192.168.1.53", ["ty=Multi"]),
            MdnsResponses.Printer("Multi", MdnsPrinterDiscoveryOptions.PdlDatastreamServiceType, "multi.local", 9100, "192.168.1.53", ["ty=Multi"]));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint).Port);
    }

    [Fact]
    public async Task DiscoverAsync_LpdOnlyPrinter_KeepsLpdPort()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            "Old", MdnsPrinterDiscoveryOptions.LpdServiceType, "old.local", 515, "192.168.1.54", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(515, Assert.IsType<NetworkPrinterEndpoint>(Assert.Single(printers).Endpoint).Port);
    }

    [Fact]
    public async Task DiscoverAsync_ResultsAreSortedByIdentifier()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.Printer("B", MdnsPrinterDiscoveryOptions.IppServiceType, "b.local", 631, "192.168.1.70", []),
            MdnsResponses.Printer("A", MdnsPrinterDiscoveryOptions.IppServiceType, "a.local", 631, "192.168.1.60", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal(["192.168.1.60", "192.168.1.70"], printers.Select(printer => printer.Id.Value));
    }

    [Fact]
    public async Task DiscoverAsync_PointerWithoutServiceRecord_IsSkipped()
    {
        FakeUdpChannel channel = new(MdnsResponses.PointerOnly(
            "Ghost", MdnsPrinterDiscoveryOptions.IppServiceType));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverAsync_MalformedAnswer_IsIgnored()
    {
        FakeUdpChannel channel = new(
            [0, 0, 0],
            MdnsResponses.Printer("Good", MdnsPrinterDiscoveryOptions.IppServiceType, "good.local", 631, "192.168.1.55", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("192.168.1.55", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_SelfPointingName_IsIgnored()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.SelfPointingName(),
            MdnsResponses.Printer("Good", MdnsPrinterDiscoveryOptions.IppServiceType, "good.local", 631, "192.168.1.56", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("192.168.1.56", Assert.Single(printers).Id.Value);
    }

    // An address that no other host can connect to must not replace the target name.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.251")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("ff02::fb")]
    public async Task DiscoverAsync_UnreachableAddressRecord_UsesServiceTargetName(string address)
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            "Odd", MdnsPrinterDiscoveryOptions.IppServiceType, "odd.local", 631, address, []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("odd.local", Assert.Single(printers).Id.Value);
    }

    // RFC 6763 §6.4: when a key appears more than one time, the first one counts.
    [Fact]
    public async Task DiscoverAsync_DuplicateTxtKey_KeepsTheFirstValue()
    {
        FakeUdpChannel channel = new(MdnsResponses.Printer(
            "Twice", MdnsPrinterDiscoveryOptions.IppServiceType, "twice.local", 631, "192.168.1.57", ["ty=First", "ty=Second"]));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Equal("First", Assert.Single(printers).Info.Name);
    }

    // One printer answer holds four records, so a cap of four stops after the first.
    [Fact]
    public async Task DiscoverAsync_MaxRecordsReached_StopsReading()
    {
        FakeUdpChannel channel = new(
            MdnsResponses.Printer("A", MdnsPrinterDiscoveryOptions.IppServiceType, "a.local", 631, "192.168.1.60", []),
            MdnsResponses.Printer("B", MdnsPrinterDiscoveryOptions.IppServiceType, "b.local", 631, "192.168.1.70", []));
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));
        var options = NewOptions();
        options.MaxRecords = 4;

        var printers = await discovery
            .DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal("192.168.1.60", Assert.Single(printers).Id.Value);
    }

    [Fact]
    public async Task DiscoverAsync_NonPositiveMaxRecords_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());
        MdnsPrinterDiscoveryOptions options = new() { MaxRecords = 0 };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAsync_NoAnswers_ReturnsEmptyWhenWindowEnds()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverAsync_SendsOneQueryPerServiceTypeAndRound()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));
        var options = NewOptions();
        options.ServiceTypes = [MdnsPrinterDiscoveryOptions.IppServiceType, MdnsPrinterDiscoveryOptions.LpdServiceType];
        options.QueryRetries = 0;

        _ = await discovery.DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(2, channel.Sent.Count);
    }

    [Fact]
    public async Task DiscoverAsync_DisposesEveryChannel()
    {
        FakeUdpChannel first = new();
        FakeUdpChannel second = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(first, second));

        _ = await discovery.DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public async Task DiscoverAsync_CallerCancellation_Propagates()
    {
        FakeUdpChannel channel = new();
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(channel));
        var options = NewOptions();
        options.BrowseTimeout = TimeSpan.FromMinutes(1);
        using CancellationTokenSource callerSource = new();
        await callerSource.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverAsync(options, callerSource.Token));
        Assert.True(channel.Disposed);
    }

    [Fact]
    public async Task DiscoverAsync_NoUsableInterface_ReturnsEmpty()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());

        var printers = await discovery
            .DiscoverAsync(NewOptions(), TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyServiceTypes_ReturnsEmpty()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory(new FakeUdpChannel()));
        var options = NewOptions();
        options.ServiceTypes = [];

        var printers = await discovery
            .DiscoverAsync(options, TestContext.Current.CancellationToken);

        Assert.Empty(printers);
    }

    [Fact]
    public async Task DiscoverAsync_NullOptions_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => discovery.DiscoverAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAsync_NonPositiveBrowseTimeout_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());
        MdnsPrinterDiscoveryOptions options = new() { BrowseTimeout = TimeSpan.Zero };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DiscoverAsync_NegativeRetries_Throws()
    {
        MdnsPrinterDiscovery discovery = new(new FakeMdnsChannelFactory());
        MdnsPrinterDiscoveryOptions options = new() { QueryRetries = -1 };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverAsync(options, TestContext.Current.CancellationToken));
    }

    private static MdnsPrinterDiscoveryOptions NewOptions() => new() { BrowseTimeout = ShortBrowse };
}
