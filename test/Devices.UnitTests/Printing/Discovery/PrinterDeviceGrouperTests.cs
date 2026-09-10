using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class PrinterDeviceGrouperTests
{
    private const string UuidText = "e3b0c442-98fc-1c14-9afb-4c8996fb9242";

    private static DiscoveredPrinter Channel(
        PrinterId id,
        PrinterEndpoint endpoint,
        DiscoverySource source = DiscoverySource.Mdns,
        string name = "Printer",
        params PrinterDeviceKey[] aliases) =>
        new(id, endpoint, new PrinterInfo(id, name)) { Source = source, Aliases = aliases };

    private static DiscoveredPrinter Raw(string host, params PrinterDeviceKey[] aliases) =>
        Channel(PrinterId.ForRaw(host), NetworkPrinterEndpoint.Raw(host), DiscoverySource.NetworkProbe, host, aliases);

    private static DiscoveredPrinter Ipp(string host, params PrinterDeviceKey[] aliases) =>
        Channel(PrinterId.ForIpp(host), NetworkPrinterEndpoint.Ipp(host), DiscoverySource.Mdns, host, aliases);

    private static DiscoveredPrinter Queue(string name, params PrinterDeviceKey[] aliases) =>
        Channel(PrinterId.ForSpooler(name), new SpoolerPrinterEndpoint(name), DiscoverySource.Spooler, name, aliases);

    [Fact]
    public void Group_ReturnsNothingForNoChannels() =>
        Assert.Empty(PrinterDeviceGrouper.Group([]));

    [Fact]
    public void Group_PutsTheTwoNetworkChannelsOfOneHostOnOneDevice()
    {
        // Neither reported an identity, so the shared host is what joins them.
        var devices = PrinterDeviceGrouper.Group([Ipp("192.168.1.5"), Raw("192.168.1.5")]);

        var device = Assert.Single(devices);
        Assert.Equal(2, device.Channels.Count);
        Assert.Equal(PrinterDeviceKeyKind.Host, device.Key.Kind);
        Assert.Equal("192.168.1.5", device.Key.Value);
    }

    [Fact]
    public void Group_JoinsAQueueToItsDeviceThroughTheAliasTheDeviceUriGave()
    {
        var uuid = PrinterDeviceKey.ForDeviceIdentity(UuidText);
        var ipp = Channel(
            PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Parse(UuidText)),
            NetworkPrinterEndpoint.Ipp("192.168.1.5"),
            DiscoverySource.Mdns,
            "Lobby",
            PrinterDeviceKey.ForHost("192.168.1.5"));
        var raw = Raw("192.168.1.5");
        var queue = Queue("EPSON_L6270", PrinterDeviceKey.ForHost("192.168.1.5"));

        var device = Assert.Single(PrinterDeviceGrouper.Group([ipp, raw, queue]));

        Assert.Equal(3, device.Channels.Count);
        // The identity outranks both addresses, so it names the device.
        Assert.Equal(uuid, device.Key);
        Assert.True(device.Key.IsDeviceIdentity);
        Assert.Equal(3, device.ChannelsByTransport.Count);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Ipp]);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Raw]);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Spooler]);
    }

    [Fact]
    public void Group_JoinsAUuidAndASerialWhenOneChannelVouchedForBoth()
    {
        var serial = PrinterDeviceKey.ForDeviceIdentity("X4TY012345");
        var ipp = Channel(
            PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Parse(UuidText)),
            NetworkPrinterEndpoint.Ipp("192.168.1.5"),
            DiscoverySource.Mdns,
            "Lobby",
            serial);
        var raw = Raw("192.168.1.9", serial);

        var device = Assert.Single(PrinterDeviceGrouper.Group([ipp, raw]));

        // A UUID is a stronger statement than a serial number.
        Assert.Equal(UuidText, device.Key.Value);
        Assert.Equal(2, device.Channels.Count);
    }

    [Fact]
    public void Group_KeepsTwoIdenticalPrintersApart()
    {
        // Same name, same model, different address, and neither vouched for the other.
        var devices = PrinterDeviceGrouper.Group([Raw("192.168.1.5"), Raw("192.168.1.6")]);

        Assert.Equal(2, devices.Count);
    }

    [Fact]
    public void Group_DoesNotJoinAQueueToAHostOfTheSameName()
    {
        // The queue is literally named after the address, and that is a coincidence, not
        // evidence. Without a device URI the two stay apart.
        var devices = PrinterDeviceGrouper.Group([Raw("192.168.1.5"), Queue("192.168.1.5")]);

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, device => device.Key.Kind == PrinterDeviceKeyKind.Host);
        Assert.Contains(devices, device => device.Key.Kind == PrinterDeviceKeyKind.Queue);
    }

    [Fact]
    public void Group_KeepsTwoDistinctIdentitiesApart()
    {
        var first = Channel(
            PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Parse(UuidText)),
            NetworkPrinterEndpoint.Ipp("192.168.1.5"));
        var second = Channel(
            PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Parse("11111111-2222-3333-4444-555555555555")),
            NetworkPrinterEndpoint.Ipp("192.168.1.6"));

        Assert.Equal(2, PrinterDeviceGrouper.Group([first, second]).Count);
    }

    [Fact]
    public void Group_OrdersTheChannelsOfADeviceByPreference()
    {
        var host = PrinterDeviceKey.ForHost("192.168.1.5");
        var devices = PrinterDeviceGrouper.Group([Raw("192.168.1.5"), Queue("Lobby", host), Ipp("192.168.1.5")]);

        var device = Assert.Single(devices);
        Assert.Equal(
            [PrinterScheme.Ipp, PrinterScheme.Spooler, PrinterScheme.Raw],
            device.Channels.Select(static channel => channel.Endpoint.Scheme));
    }

    [Fact]
    public void Group_IsStableWhateverOrderTheSourcesAnsweredIn()
    {
        var host = PrinterDeviceKey.ForHost("192.168.1.5");
        var forward = PrinterDeviceGrouper.Group([Ipp("192.168.1.5"), Raw("192.168.1.5"), Queue("Lobby", host)]);
        var backward = PrinterDeviceGrouper.Group([Queue("Lobby", host), Raw("192.168.1.5"), Ipp("192.168.1.5")]);

        Assert.Equal(
            forward.Select(static device => device.Key),
            backward.Select(static device => device.Key));
        Assert.Equal(
            forward[0].Channels.Select(static channel => channel.Id),
            backward[0].Channels.Select(static channel => channel.Id));
    }

    [Fact]
    public void Group_ReportsEverySourceThatContributed()
    {
        var host = PrinterDeviceKey.ForHost("192.168.1.5");
        var device = Assert.Single(PrinterDeviceGrouper.Group([Ipp("192.168.1.5"), Raw("192.168.1.5"), Queue("Lobby", host)]));

        Assert.Equal(
            [DiscoverySource.Mdns, DiscoverySource.NetworkProbe, DiscoverySource.Spooler],
            device.Details.ContributedBy.Order());
    }

    [Fact]
    public void Group_ReportsTheReadOnlyProtocolsThatAnswered()
    {
        var ipp = Ipp("192.168.1.5");
        var raw = Raw("192.168.1.5");
        Dictionary<PrinterId, IReadOnlyList<PrinterStatusSource>> sources = new()
        {
            [ipp.Id] = [PrinterStatusSource.Ipp],
            [raw.Id] = [PrinterStatusSource.Snmp],
        };

        var device = Assert.Single(PrinterDeviceGrouper.Group([ipp, raw], sources));

        // SNMP is not a channel a job can take, so it shows up here and nowhere else.
        Assert.Equal([PrinterStatusSource.Ipp, PrinterStatusSource.Snmp], device.Details.StatusSources);
    }
}
