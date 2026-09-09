using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaptArch.Devices.DependencyInjection.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPrinters_RegistersTransportAndDiscoveryAsSingletons()
    {
        ServiceCollection services = new();

        var result = services.AddPrinters();

        Assert.Same(services, result);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>());
        Assert.IsType<TcpNetworkPrinterDiscovery>(provider.GetRequiredService<INetworkPrinterDiscovery>());
        Assert.IsType<MdnsPrinterDiscovery>(provider.GetRequiredService<IMdnsPrinterDiscovery>());
        Assert.IsType<IppPrinterStatusClient>(provider.GetRequiredService<IppPrinterStatusClient>());
        Assert.IsType<SnmpPrinterStatusClient>(provider.GetRequiredService<SnmpPrinterStatusClient>());
        Assert.Same(
            provider.GetRequiredService<IPrinterTransport>(),
            provider.GetRequiredService<IPrinterTransport>());
        Assert.Same(
            provider.GetRequiredService<IMdnsPrinterDiscovery>(),
            provider.GetRequiredService<IMdnsPrinterDiscovery>());
        Assert.Same(
            provider.GetRequiredService<SnmpPrinterStatusClient>(),
            provider.GetRequiredService<SnmpPrinterStatusClient>());
    }

    [Fact]
    public void AddDevices_RegistersPrinterServices()
    {
        ServiceCollection services = new();

        services.AddDevices();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>());
        Assert.IsType<TcpNetworkPrinterDiscovery>(provider.GetRequiredService<INetworkPrinterDiscovery>());
        Assert.IsType<MdnsPrinterDiscovery>(provider.GetRequiredService<IMdnsPrinterDiscovery>());
        Assert.IsType<IppPrinterStatusClient>(provider.GetRequiredService<IppPrinterStatusClient>());
        Assert.IsType<SnmpPrinterStatusClient>(provider.GetRequiredService<SnmpPrinterStatusClient>());
    }

    [Fact]
    public void AddPrinters_ResolvesTheNewPrintingServices()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        _ = Assert.IsType<PrinterFactory>(provider.GetRequiredService<IPrinterFactory>());
        _ = Assert.IsType<SpoolerPrinterDiscovery>(provider.GetRequiredService<IPrinterDiscovery>());
        _ = Assert.IsType<CompositePrintJobQueue>(provider.GetRequiredService<IPrintJobQueue>());
        _ = Assert.IsType<PollingPrintJobMonitor>(provider.GetRequiredService<IPrintJobMonitor>());
    }

    [Fact]
    public void AddPrinters_HoldsTheNewServicesAsSingletons()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IPrinterFactory>(), provider.GetRequiredService<IPrinterFactory>());
        Assert.Same(provider.GetRequiredService<IPrintJobQueue>(), provider.GetRequiredService<IPrintJobQueue>());
        Assert.Same(provider.GetRequiredService<IPrintJobMonitor>(), provider.GetRequiredService<IPrintJobMonitor>());
    }
}
