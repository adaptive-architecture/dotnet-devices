using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// The largest datagram that a channel reads. RFC 6762 permits a multicast DNS
    /// response of up to 9000 bytes.
    /// </summary>
    private const int MaxDatagramSize = 9000;

    /// <inheritdoc />
    public IReadOnlyList<(IUdpChannel Channel, IPEndPoint Destination)> Create(MdnsPrinterDiscoveryOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<(IUdpChannel, IPEndPoint)> channels = [];
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (!IsUsable(adapter, options))
            {
                continue;
            }

            // One channel per address family: a second address of the same family is on
            // the same link, so it would query the same printers again.
            var properties = adapter.GetIPProperties();
            var v4 = FirstAddress(properties, AddressFamily.InterNetwork);
            if (v4 is not null)
            {
                AddChannel(channels, v4, GroupV4, 0, logger);
            }

            var v6 = options.IncludeIPv6 ? FirstAddress(properties, AddressFamily.InterNetworkV6) : null;
            if (v6 is not null && adapter.Supports(NetworkInterfaceComponent.IPv6) && TryGetIndex(properties, AddressFamily.InterNetworkV6, out var v6Index))
            {
                AddChannel(channels, v6, GroupV6, v6Index, logger);
            }
        }

        return channels;
    }

    private static IPAddress? FirstAddress(IPInterfaceProperties properties, AddressFamily family) =>
        properties.UnicastAddresses
            .Select(unicast => unicast.Address)
            .FirstOrDefault(address => address.AddressFamily == family && !IPAddress.IsLoopback(address));

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

        // An adapter whose index cannot be read matches no index the caller named, so it is
        // skipped rather than allowed to fail the browse.
        return TryGetIndex(adapter, out var index) && options.NetworkInterfaceIndexes.Contains(index);
    }

    // The index of an adapter, or false when neither family answers for it.
    //
    // GetIPv4Properties and GetIPv6Properties throw NetworkInformationException when the
    // family is not configured on that adapter, and NetworkInterface.Supports does not
    // promise otherwise: a Windows machine with a tunnel or a virtual adapter reports
    // support and then refuses the properties. One such adapter used to take the whole
    // browse down, which is a discovery that finds nothing on a machine that has printers.
    private static bool TryGetIndex(NetworkInterface adapter, out int index)
    {
        var properties = adapter.GetIPProperties();
        if (adapter.Supports(NetworkInterfaceComponent.IPv4) && TryGetIndex(properties, AddressFamily.InterNetwork, out index))
        {
            return true;
        }

        return TryGetIndex(properties, AddressFamily.InterNetworkV6, out index);
    }

    private static bool TryGetIndex(IPInterfaceProperties properties, AddressFamily family, out int index)
    {
        try
        {
            index = family == AddressFamily.InterNetwork
                ? properties.GetIPv4Properties().Index
                : properties.GetIPv6Properties().Index;
            return true;
        }
        catch (NetworkInformationException)
        {
            index = 0;
            return false;
        }
    }

    private static void AddChannel(
        List<(IUdpChannel, IPEndPoint)> channels,
        IPAddress address,
        IPAddress group,
        int interfaceIndex,
        ILogger logger)
    {
        try
        {
            UdpChannel channel = new(address, MaxDatagramSize, TimeToLive, interfaceIndex);
            channels.Add((channel, new IPEndPoint(group, Port)));
        }
        catch (SocketException exception)
        {
            // "Discovery found nothing on this machine" is usually answered here.
            DiscoveryLog.InterfaceRefused(logger, address, exception);
            // One interface that refuses a socket must not stop the browse on the others.
        }
    }
}
