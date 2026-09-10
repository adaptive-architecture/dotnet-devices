using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class TcpNetworkPrinterDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_FindsOpenPort()
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

        var found = await discovery.DiscoverAsync(options, timeoutSource.Token);

        var printer = Assert.Single(found);
        Assert.Equal(PrinterScheme.Raw, printer.Id.Scheme);
        Assert.True(printer.Id.TryGetHost(out var host));
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(DiscoverySource.NetworkProbe, printer.Source);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(printer.Endpoint);
        Assert.Equal(port, endpoint.Port);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsClosedPorts()
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

        var found = await discovery.DiscoverAsync(options, timeoutSource.Token);

        Assert.Empty(found);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyHosts_ReturnsEmpty()
    {
        TcpNetworkPrinterDiscovery discovery = new();

        var found = await discovery
            .DiscoverAsync(new NetworkPrinterDiscoveryOptions(), CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task DiscoverAsync_SkipsBlankHosts()
    {
        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new() { Hosts = ["  ", String.Empty] };
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));

        var found = await discovery.DiscoverAsync(options, timeoutSource.Token);

        Assert.Empty(found);
    }

    [Fact]
    public async Task DiscoverAsync_DuplicateHosts_ProbesAndReportsOnce()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(15));

        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new()
        {
            Hosts = ["127.0.0.1", "127.0.0.1"],
            Port = port,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        var found = await discovery.DiscoverAsync(options, timeoutSource.Token);

        Assert.True(Assert.Single(found).Id.TryGetHost(out var host));
        Assert.Equal("127.0.0.1", host);
    }

    [Fact]
    public async Task DiscoverAsync_NonPositiveConnectTimeout_Throws()
    {
        TcpNetworkPrinterDiscovery discovery = new();
        NetworkPrinterDiscoveryOptions options = new() { Hosts = ["127.0.0.1"], ConnectTimeout = TimeSpan.Zero };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => discovery.DiscoverAsync(options, CancellationToken.None));
    }

    private static int GetClosedPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // The result depends on the adapters of the machine, so only the shape is asserted.
    [Fact]
    public void LocalSubnetHosts_GivesASortedListWithoutADuplicate()
    {
        var hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts();

        Assert.Equal(hosts.Distinct(StringComparer.Ordinal).Count(), hosts.Count);
        Assert.Equal(hosts.Order(StringComparer.Ordinal), hosts);
        Assert.All(hosts, static host => Assert.True(IPAddress.TryParse(host, out _)));
    }

    [Fact]
    public void LocalSubnetHosts_StopsAtTheMaximum() =>
        Assert.True(NetworkPrinterDiscoveryOptions.LocalSubnetHosts(4).Count <= 4);

    [Fact]
    public void LocalSubnetHosts_RefusesAMaximumThatIsNotPositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkPrinterDiscoveryOptions.LocalSubnetHosts(0));
}
