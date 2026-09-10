using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterManagerEnrichmentTests
{
    private const string UuidText = "e3248000-80ce-11db-8000-3c2af4a0d21d";

    private static PrinterManager Manager(FakePrinterFactory factory, params DiscoveredPrinter[] found) =>
        new(new FakeMdnsDiscovery(found), new FakeSpoolerDiscovery([]), new FakeNetworkProbe([]), factory, new FakePrintJobMonitor([]));

    [Fact]
    public async Task DiscoverAsync_ReadsNothingByDefault()
    {
        FakePrinterFactory factory = new();
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        // Nothing was opened, because neither read was asked for.
        Assert.Empty(factory.Opened);
        Assert.Null(Assert.Single(Assert.Single(devices).Channels).Configuration);
    }

    [Fact]
    public async Task DiscoverAsync_ReadCapabilitiesFillsTheConfigurationOfEachChannelOnce()
    {
        FakePrinterFactory factory = new()
        {
            ConfigurationOf = channel => new PrinterConfiguration(channel.Id)
            {
                SupportsDuplex = true,
                SupportedResolutionsDpi = [300, 600],
            },
        };
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadCapabilities = true }, TestContext.Current.CancellationToken);

        var channel = Assert.Single(Assert.Single(devices).Channels);
        Assert.NotNull(channel.Configuration);
        Assert.Equal([300, 600], channel.Configuration.SupportedResolutionsDpi);
        Assert.Equal(1, Assert.Single(factory.Printers).ConfigurationCalls);
    }

    [Fact]
    public async Task DiscoverAsync_ReadCapabilitiesNarrowsTheOptionsAChannelApplies()
    {
        FakePrinterFactory factory = new()
        {
            ConfigurationOf = channel => new PrinterConfiguration(channel.Id) { SupportsDuplex = false, SupportsColor = false, SupportsPageRanges = false },
        };
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadCapabilities = true }, TestContext.Current.CancellationToken);

        var channel = Assert.Single(Assert.Single(devices).Channels);
        Assert.False(channel.SupportedOptions.HasFlag(PrintOptionSupport.Duplex));
        Assert.False(channel.SupportedOptions.HasFlag(PrintOptionSupport.ColorMode));
        Assert.False(channel.SupportedOptions.HasFlag(PrintOptionSupport.PageRanges));
        // Everything the printer did not deny is still applied.
        Assert.True(channel.SupportedOptions.HasFlag(PrintOptionSupport.Copies));
    }

    [Fact]
    public async Task DiscoverAsync_ARawChannelAppliesNoOptionAtAll()
    {
        var manager = Manager(new FakePrinterFactory(), FakePrinters.Raw("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(PrintOptionSupport.None, Assert.Single(Assert.Single(devices).Channels).SupportedOptions);
    }

    [Fact]
    public async Task DiscoverAsync_ReadIdentityMergesTwoChannelsIntoOneDevice()
    {
        // Two hosts, one printer: only the identity each channel reports says so.
        var first = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        var second = FakePrinters.Raw("192.168.1.99", DiscoverySource.Mdns);
        FakePrinterFactory factory = new()
        {
            IdentityOf = _ => new PrinterIdentity { Uuid = UuidText, Manufacturer = "EPSON", Model = "L6270" },
        };
        var manager = Manager(factory, first, second);

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadIdentity = true }, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices);
        Assert.Equal(2, device.Channels.Count);
        Assert.Equal(PrinterDeviceKey.ForDeviceIdentity(UuidText), device.Key);
        Assert.Equal(UuidText, device.Details.Uuid);
        Assert.Equal("EPSON", device.Details.Manufacturer);
        Assert.Equal("L6270", device.Details.Model);
    }

    [Fact]
    public async Task DiscoverAsync_ReadIdentityKeepsTheOldAddressResolvable()
    {
        var channel = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        FakePrinterFactory factory = new() { IdentityOf = _ => new PrinterIdentity { Uuid = UuidText } };
        var manager = Manager(factory, channel);

        _ = await manager.DiscoverAsync(new PrinterManagerOptions { ReadIdentity = true }, TestContext.Current.CancellationToken);
        var status = await manager.GetStatusAsync(channel.Id, TestContext.Current.CancellationToken);

        // The identifier the caller kept from before the identity was read still works.
        Assert.Equal(PrinterStatusState.Idle, status.State);
    }

    [Fact]
    public async Task DiscoverAsync_ReadIdentityReportsWhichProtocolAnswered()
    {
        FakePrinterFactory factory = new() { IdentityOf = _ => new PrinterIdentity { Uuid = UuidText } };
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadIdentity = true }, TestContext.Current.CancellationToken);

        Assert.Equal([PrinterStatusSource.Ipp], Assert.Single(devices).Details.StatusSources);
    }

    [Fact]
    public async Task DiscoverAsync_ASerialNumberGroupsTheDeviceButStaysOutOfTheIdentifier()
    {
        // A serial number reads exactly like a host name, so writing it into an authority
        // would make the text ambiguous. It still merges the two channels.
        var first = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        var second = FakePrinters.Raw("192.168.1.99", DiscoverySource.Mdns);
        FakePrinterFactory factory = new() { IdentityOf = _ => new PrinterIdentity { SerialNumber = "X4TY012345" } };
        var manager = Manager(factory, first, second);

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadIdentity = true }, TestContext.Current.CancellationToken);

        var device = Assert.Single(devices);
        Assert.Equal(PrinterDeviceKey.ForDeviceIdentity("X4TY012345"), device.Key);
        Assert.All(device.Channels, channel => Assert.False(channel.Id.IsDeviceIdentity));
    }

    [Fact]
    public async Task DiscoverAsync_APlaceholderSerialNeverMergesAFleet()
    {
        var first = FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns);
        var second = FakePrinters.Ipp("192.168.1.99", DiscoverySource.Mdns);
        FakePrinterFactory factory = new() { IdentityOf = _ => new PrinterIdentity { SerialNumber = "n/a" } };
        var manager = Manager(factory, first, second);

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadIdentity = true }, TestContext.Current.CancellationToken);

        Assert.Equal(2, devices.Count);
    }

    [Fact]
    public async Task DiscoverAsync_AChannelThatDoesNotAnswerDoesNotFailTheDiscovery()
    {
        // Null makes the fake configuration read throw.
        FakePrinterFactory factory = new() { ConfigurationOf = _ => null };
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadCapabilities = true }, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(Assert.Single(devices).Channels).Configuration);
    }

    [Fact]
    public async Task DiscoverAsync_AChannelThatCannotBeOpenedDoesNotFailTheDiscovery()
    {
        FakePrinterFactory factory = new() { FailOpen = true };
        var manager = Manager(factory, FakePrinters.Ipp("192.168.1.50", DiscoverySource.Mdns));

        var devices = await manager.DiscoverAsync(
            new PrinterManagerOptions { ReadCapabilities = true, ReadIdentity = true }, TestContext.Current.CancellationToken);

        _ = Assert.Single(devices);
    }

    [Fact]
    public async Task DiscoverAsync_RefusesAConcurrencyBelowOne()
    {
        var manager = Manager(new FakePrinterFactory());

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.DiscoverAsync(
            new PrinterManagerOptions { MaxEnrichmentConcurrency = 0 }, TestContext.Current.CancellationToken));
    }
}
