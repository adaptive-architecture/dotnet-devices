using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaptArch.Devices.DependencyInjection.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPrinters_RegistersTransportAndDiscoveryAsSingletons()
    {
        ServiceCollection services = new();

        IServiceCollection result = services.AddPrinters();

        Assert.Same(services, result);
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>());
        Assert.IsType<TcpNetworkPrinterDiscovery>(provider.GetRequiredService<INetworkPrinterDiscovery>());
        Assert.IsType<IppPrinterStatusClient>(provider.GetRequiredService<IppPrinterStatusClient>());
        Assert.Same(
            provider.GetRequiredService<IPrinterTransport>(),
            provider.GetRequiredService<IPrinterTransport>());
    }

    [Fact]
    public void AddDevices_RegistersPrinterServices()
    {
        ServiceCollection services = new();

        services.AddDevices();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>());
        Assert.IsType<TcpNetworkPrinterDiscovery>(provider.GetRequiredService<INetworkPrinterDiscovery>());
        Assert.IsType<IppPrinterStatusClient>(provider.GetRequiredService<IppPrinterStatusClient>());
    }
}
