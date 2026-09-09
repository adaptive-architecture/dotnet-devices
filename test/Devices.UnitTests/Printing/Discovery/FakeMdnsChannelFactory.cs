using System.Net;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

internal sealed class FakeMdnsChannelFactory : IMdnsChannelFactory
{
    private readonly IUdpChannel[] _channels;

    public FakeMdnsChannelFactory(params IUdpChannel[] channels) => _channels = channels;

    public IReadOnlyList<(IUdpChannel Channel, IPEndPoint Destination)> Create(MdnsPrinterDiscoveryOptions options)
    {
        List<(IUdpChannel, IPEndPoint)> created = [];
        foreach (var channel in _channels)
        {
            created.Add((channel, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353)));
        }

        return created;
    }
}
