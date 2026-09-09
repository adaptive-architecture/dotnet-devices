using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class TcpNetworkPrinterDiscoveryTests
{
    [Fact]
    public async Task DiscoverNetworkPrintersAsync_FindsOpenPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));

        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new()
        {
            Hosts = ["127.0.0.1"],
            Port = port,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        var found = await discovery.DiscoverNetworkPrintersAsync(options, timeoutSource.Token);

        var printer = Assert.Single(found);
        Assert.Equal(PrinterIdKind.Network, printer.Id.Kind);
        Assert.Equal("127.0.0.1", printer.Id.Value);
        Assert.Equal(DiscoverySource.NetworkProbe, printer.Source);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint);
        Assert.Equal(port, endpoint.Port);
    }

    [Fact]
    public async Task DiscoverNetworkPrintersAsync_SkipsClosedPorts()
    {
        var closedPort = GetClosedPort();
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));

        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new()
        {
            Hosts = ["127.0.0.1"],
            Port = closedPort,
            ConnectTimeout = TimeSpan.FromSeconds(2),
        };

        var found = await discovery.DiscoverNetworkPrintersAsync(options, timeoutSource.Token);

        Assert.Empty(found);
    }

    [Fact]
    public async Task DiscoverNetworkPrintersAsync_EmptyHosts_ReturnsEmpty()
    {
        TcpNetworkPrinterDiscovery discovery = new();

        var found = await discovery
            .DiscoverNetworkPrintersAsync(new NetworkPrinterDiscoveryOptions(), CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task DiscoverNetworkPrintersAsync_SkipsBlankHosts()
    {
        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new() { Hosts = ["  ", String.Empty] };
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));

        var found = await discovery.DiscoverNetworkPrintersAsync(options, timeoutSource.Token);

        Assert.Empty(found);
    }

    private static int GetClosedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
