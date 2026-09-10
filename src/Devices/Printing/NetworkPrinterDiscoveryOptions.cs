using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for direct network printer discovery via TCP probing.
/// Probing is opt-in by design: it opens connections to every listed host,
/// so callers control the scope, timeouts, and parallelism.
/// </summary>
public sealed class NetworkPrinterDiscoveryOptions
{
    /// <summary>
    /// Gets or sets the host names or IP addresses to probe.
    /// </summary>
    public IReadOnlyList<string> Hosts { get; set; } = [];

    /// <summary>
    /// Gets or sets the TCP port to probe. Defaults to 9100 (raw print channel).
    /// </summary>
    public int Port { get; set; } = NetworkPrinterEndpoint.DefaultPort;

    /// <summary>
    /// Gets or sets the channel the probe reports. Defaults to <see cref="PrinterScheme.Raw"/>,
    /// which matches the default port. Set it to <see cref="PrinterScheme.Ipp"/> when the
    /// probe targets the IPP port, so the identifier names the channel that answered.
    /// </summary>
    public PrinterScheme Scheme { get; set; } = PrinterScheme.Raw;

    /// <summary>
    /// Gets or sets the per-host connection timeout. Defaults to one second.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum number of concurrent probes. Defaults to 32.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 32;

    /// <summary>
    /// Lists every IPv4 address of the local subnets, to fill <see cref="Hosts"/> with.
    /// </summary>
    /// <param name="maxHosts">The maximum number of addresses to return. Defaults to 4096.</param>
    /// <returns>The addresses, sorted and without a duplicate.</returns>
    /// <remarks>
    /// The method reads the network adapters only. It opens no connection and sends
    /// nothing, so the caller still decides the scope of the probe.
    /// <para>
    /// An adapter counts only when it is up, has an IPv4 gateway, and has a prefix
    /// length from 16 to 30. A loopback address and a link-local address are skipped.
    /// A prefix shorter than 16 holds too many addresses to probe, and a prefix longer
    /// than 30 holds no host. IPv6 is not listed: a subnet there is too large to walk.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxHosts"/> is not positive.</exception>
    public static IReadOnlyList<string> LocalSubnetHosts(int maxHosts = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHosts);

        HashSet<string> hosts = new(StringComparer.Ordinal);
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var properties = adapter.GetIPProperties();
            if (!HasGateway(properties))
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                AddSubnetHosts(hosts, unicast, maxHosts);
            }
        }

        List<string> result = [.. hosts];
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    // Walks the subnet of one address, between the network address and the broadcast
    // address. An address the probe must not sweep adds nothing.
    private static void AddSubnetHosts(HashSet<string> hosts, UnicastIPAddressInformation unicast, int maxHosts)
    {
        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
        {
            return;
        }

        var addressBytes = unicast.Address.GetAddressBytes();
        if (IPAddress.IsLoopback(unicast.Address) || IsLinkLocal(addressBytes))
        {
            return;
        }

        if (unicast.PrefixLength < 16 || unicast.PrefixLength > 30)
        {
            return;
        }

        var address = ReadUInt32(addressBytes);
        var mask = 0xFFFFFFFFu << (32 - unicast.PrefixLength);
        var network = address & mask;
        var broadcast = network | ~mask;
        for (var host = network + 1; host < broadcast && hosts.Count < maxHosts; host++)
        {
            _ = hosts.Add(ToAddress(host));
        }
    }

    private static bool IsLinkLocal(byte[] addressBytes) => addressBytes[0] == 169 && addressBytes[1] == 254;

    private static bool HasGateway(IPInterfaceProperties properties) => properties.GatewayAddresses.Any(
        static gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

    private static uint ReadUInt32(byte[] addressBytes) =>
        ((uint)addressBytes[0] << 24) | ((uint)addressBytes[1] << 16) | ((uint)addressBytes[2] << 8) | addressBytes[3];

    private static string ToAddress(uint address) => new IPAddress(
        [(byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address]).ToString();
}
