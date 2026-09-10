using System.Net.NetworkInformation;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class MdnsChannelFactoryTests
{
    [Fact]
    public void Create_UnknownInterfaceIndex_ReturnsEmpty()
    {
        MdnsPrinterDiscoveryOptions options = new() { NetworkInterfaceIndexes = [Int32.MaxValue] };

        var channels = new MdnsChannelFactory().Create(options);

        Assert.Empty(channels);
    }

    // A second address of the same family is on the same link, so it gets no channel.
    [Fact]
    public void Create_OpensAtMostOneChannelPerAdapterAndAddressFamily()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces().Count(static adapter =>
            adapter.OperationalStatus == OperationalStatus.Up &&
            adapter.SupportsMulticast &&
            adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        var channels = new MdnsChannelFactory().Create(new MdnsPrinterDiscoveryOptions { IncludeIPv6 = true });
        try
        {
            Assert.True(channels.Count <= 2 * adapters, $"{channels.Count} channels for {adapters} adapters.");
        }
        finally
        {
            foreach ((var channel, _) in channels)
            {
                channel.Dispose();
            }
        }
    }
}
