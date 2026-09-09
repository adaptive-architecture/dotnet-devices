using System.Net;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Creates the sockets that a multicast DNS browse sends on, one for each selected
/// network interface. This seam lets a unit test supply a fake channel.
/// </summary>
internal interface IMdnsChannelFactory
{
    /// <summary>
    /// Creates one channel for each network interface that the options select, with the
    /// multicast group address to send to.
    /// </summary>
    /// <param name="options">The interface selection and the address family choice.</param>
    /// <returns>The channels and their destinations. Empty when no interface is usable.</returns>
    IReadOnlyList<(IUdpChannel Channel, IPEndPoint Destination)> Create(MdnsPrinterDiscoveryOptions options);
}
