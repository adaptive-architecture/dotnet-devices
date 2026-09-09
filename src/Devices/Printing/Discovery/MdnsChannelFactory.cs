using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Creates a real socket for each network interface that can carry a multicast DNS query.
/// </summary>
internal sealed class MdnsChannelFactory : IMdnsChannelFactory
{
    /// <summary>
    /// The IPv4 multicast group for multicast DNS.
    /// </summary>
    [SuppressMessage(
        "Major Security Hotspot",
        "S1313:IP addresses should not be hardcoded",
        Justification = "RFC 6762 assigns this address as the multicast DNS group; a caller cannot change it and still be speaking mDNS.")]
    private static readonly IPAddress GroupV4 = IPAddress.Parse("224.0.0.251");

    /// <summary>
    /// The IPv6 multicast group for multicast DNS.
    /// </summary>
    [SuppressMessage(
        "Major Security Hotspot",
        "S1313:IP addresses should not be hardcoded",
        Justification = "RFC 6762 assigns this address as the multicast DNS group; a caller cannot change it and still be speaking mDNS.")]
    private static readonly IPAddress GroupV6 = IPAddress.Parse("ff02::fb");

    /// <summary>
    /// The port that multicast DNS uses.
    /// </summary>
    private const int Port = 5353;

    /// <summary>
    /// RFC 6762 requires this time to live, so that no router reduces the count.
    /// </summary>
    private const int TimeToLive = 255;

    /// <inheritdoc />
    public IReadOnlyList<(IUdpChannel Channel, IPEndPoint Destination)> Create(MdnsPrinterDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<(IUdpChannel, IPEndPoint)> channels = [];
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsUsable(adapter, options))
            {
                continue;
            }

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                AddChannel(channels, unicast.Address, options);
            }
        }

        return channels;
    }

    private static bool IsUsable(NetworkInterface adapter, MdnsPrinterDiscoveryOptions options)
    {
        if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.SupportsMulticast)
        {
            return false;
        }

        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
        {
            return false;
        }

        if (options.NetworkInterfaceIndexes.Count == 0)
        {
            return true;
        }

        return options.NetworkInterfaceIndexes.Contains(GetIndex(adapter));
    }

    private static int GetIndex(NetworkInterface adapter)
    {
        var properties = adapter.GetIPProperties();
        if (adapter.Supports(NetworkInterfaceComponent.IPv4))
        {
            return properties.GetIPv4Properties().Index;
        }

        return properties.GetIPv6Properties().Index;
    }

    private static void AddChannel(
        List<(IUdpChannel, IPEndPoint)> channels,
        IPAddress address,
        MdnsPrinterDiscoveryOptions options)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !options.IncludeIPv6)
        {
            return;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork &&
            address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return;
        }

        if (IPAddress.IsLoopback(address))
        {
            return;
        }

        var group = address.AddressFamily == AddressFamily.InterNetwork ? GroupV4 : GroupV6;
        try
        {
            UdpChannel channel = new(address, TimeToLive);
            channels.Add((channel, new IPEndPoint(group, Port)));
        }
        catch (SocketException)
        {
            // One interface that refuses a socket must not stop the browse on the others.
        }
    }
}
