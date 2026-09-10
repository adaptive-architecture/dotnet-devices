using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterDeviceTests
{
    private static DiscoveredPrinter Channel(PrinterId id, PrinterEndpoint endpoint, PrinterInfo info, DiscoverySource source = DiscoverySource.Mdns) =>
        new(id, endpoint, info) { Source = source };

    private static DiscoveredPrinter Ipp(string host, Action<PrinterInfoBuilder> configure = null)
    {
        var id = PrinterId.ForIpp(host);
        return Channel(id, NetworkPrinterEndpoint.Ipp(host), Build(id, host, configure));
    }

    private static DiscoveredPrinter Raw(string host, Action<PrinterInfoBuilder> configure = null)
    {
        var id = PrinterId.ForRaw(host);
        return Channel(id, NetworkPrinterEndpoint.Raw(host), Build(id, host, configure), DiscoverySource.NetworkProbe);
    }

    private static DiscoveredPrinter Queue(string name, Action<PrinterInfoBuilder> configure = null)
    {
        var id = PrinterId.ForSpooler(name);
        return Channel(id, new SpoolerPrinterEndpoint(name), Build(id, name, configure), DiscoverySource.Spooler);
    }

    private sealed class PrinterInfoBuilder
    {
        public string Name { get; set; }

        public string Location { get; set; }

        public string Model { get; set; }

        public string SerialNumber { get; set; }
    }

    private static PrinterInfo Build(PrinterId id, string fallback, Action<PrinterInfoBuilder> configure)
    {
        PrinterInfoBuilder builder = new() { Name = fallback };
        configure?.Invoke(builder);
        return new PrinterInfo(id, builder.Name)
        {
            Location = builder.Location,
            Model = builder.Model,
            SerialNumber = builder.SerialNumber,
        };
    }

    [Fact]
    public void Constructor_RefusesADeviceWithNoChannel() =>
        Assert.Throws<ArgumentException>(() => new PrinterDevice(PrinterDeviceKey.ForHost("h"), []));

    [Fact]
    public void Channels_ArePutInTheOrderTheManagerUses()
    {
        PrinterDevice device = new(PrinterDeviceKey.ForHost("192.168.1.5"), [Raw("192.168.1.5"), Queue("Lobby"), Ipp("192.168.1.5")]);

        Assert.Equal(
            [PrinterScheme.Ipp, PrinterScheme.Spooler, PrinterScheme.Raw],
            device.Channels.Select(static channel => channel.Endpoint.Scheme));
    }

    [Fact]
    public void ChannelsByTransport_GroupsEveryChannel()
    {
        PrinterDevice device = new(PrinterDeviceKey.ForHost("192.168.1.5"), [Raw("192.168.1.5"), Ipp("192.168.1.5"), Queue("Lobby")]);

        Assert.Equal(3, device.ChannelsByTransport.Count);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Raw]);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Ipp]);
        Assert.Single(device.ChannelsByTransport[PrinterScheme.Spooler]);
    }

    [Fact]
    public void Details_TakeAHumanFactFromTheSpoolerFirst()
    {
        // The queue name is what the operating system already shows the user; the device
        // answers with a model number.
        var queue = Queue("Lobby", builder => { builder.Name = "Reception printer"; builder.Location = "Reception"; });
        var ipp = Ipp("192.168.1.5", builder => { builder.Name = "EPSON L6270 Series"; builder.Location = "Room 2"; });
        PrinterDevice device = new(PrinterDeviceKey.ForHost("192.168.1.5"), [ipp, queue]);

        Assert.Equal("Reception printer", device.Details.Name);
        Assert.Equal("Reception", device.Details.Location);
    }

    [Fact]
    public void Details_TakeAHardwareFactFromTheDeviceFirst()
    {
        var queue = Queue("Lobby", builder => { builder.Model = "Generic PostScript"; builder.SerialNumber = "queue-guess"; });
        var ipp = Ipp("192.168.1.5", builder => { builder.Model = "L6270"; builder.SerialNumber = "X4TY012345"; });
        PrinterDevice device = new(PrinterDeviceKey.ForHost("192.168.1.5"), [queue, ipp]);

        Assert.Equal("L6270", device.Details.Model);
        Assert.Equal("X4TY012345", device.Details.SerialNumber);
    }

    [Fact]
    public void Details_NeverInventAValueNoChannelReported()
    {
        PrinterDevice device = new(PrinterDeviceKey.ForHost("192.168.1.5"), [Raw("192.168.1.5")]);

        Assert.Null(device.Details.Uuid);
        Assert.Null(device.Details.SerialNumber);
        Assert.Null(device.Details.Manufacturer);
        Assert.Empty(device.Details.StatusSources);
    }

    [Fact]
    public void Id_PrefersTheIdentityFormWhenAChannelHasOne()
    {
        var uuid = Guid.Parse("e3248000-80ce-11db-8000-3c2af4a0d21d");
        var identityId = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid);
        var identity = Channel(identityId, NetworkPrinterEndpoint.Ipp("192.168.1.5"), new PrinterInfo(identityId, "Lobby"));
        PrinterDevice device = new(PrinterDeviceKey.ForDeviceIdentity(uuid.ToString("D")), [Raw("192.168.1.5"), identity]);

        Assert.Equal(identityId, device.Id);
    }

    [Fact]
    public void HasJobQueueAndGivesPassthrough_ReadTheWholeDevice()
    {
        PrinterDevice rawOnly = new(PrinterDeviceKey.ForHost("192.168.1.5"), [Raw("192.168.1.5")]);
        Assert.False(rawOnly.HasJobQueue);
        Assert.True(rawOnly.GivesPassthrough);

        PrinterDevice ippOnly = new(PrinterDeviceKey.ForHost("192.168.1.6"), [Ipp("192.168.1.6")]);
        Assert.True(ippOnly.HasJobQueue);
        Assert.False(ippOnly.GivesPassthrough);

        PrinterDevice both = new(PrinterDeviceKey.ForHost("192.168.1.7"), [Raw("192.168.1.7"), Ipp("192.168.1.7")]);
        Assert.True(both.HasJobQueue);
        Assert.True(both.GivesPassthrough);
    }
}
