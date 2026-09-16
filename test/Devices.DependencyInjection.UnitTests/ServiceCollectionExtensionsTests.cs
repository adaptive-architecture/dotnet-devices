using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void AddPrinters_ResolvesTheManagerWithTheConfiguredPolicy()
    {
        // The manager has two constructors. The options must be registered, or the
        // container picks the one without them and the policy is silently lost.
        ServiceCollection services = new();

        services.AddPrinters(configureManager: options => options.Transports = [PrinterScheme.Spooler]);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<PrinterManager>(provider.GetRequiredService<IPrinterManager>());
        Assert.Equal([PrinterScheme.Spooler], provider.GetRequiredService<PrinterManagerOptions>().Transports);
    }

    [Fact]
    public void AddPrinters_DefaultsToEveryTransport()
    {
        ServiceCollection services = new();

        services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Equal(PrinterManagerOptions.DefaultTransports, provider.GetRequiredService<PrinterManagerOptions>().Transports);
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

    [Fact]
    public void AddPrinters_CalledTwice_RegistersEveryServiceOnce()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();
        _ = services.AddDevices();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPrinterFactory));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IPrintJobQueue));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IppPrinterStatusClient));
    }

    [Fact]
    public void AddPrinters_PassesTheConfiguredTransportOptionsToTheContainer()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters(options => options.AllowPlainIpp = false);

        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IppTransportOptions>().AllowPlainIpp);
    }

    [Fact]
    public async Task AddPrinters_DisposesTheSharedClientWithTheContainer()
    {
        ServiceCollection services = new();
        _ = services.AddPrinters();
        var provider = services.BuildServiceProvider();
        var holder = provider.GetRequiredService<ServiceCollectionExtensions.IppHttpClientHolder>();

        provider.Dispose();

        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => holder.Client.GetAsync("http://printer.local/", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AddPrinters_TakesTheLoggerFactoryOfTheContainer()
    {
        ServiceCollection services = new();
        ILoggerFactory factory = NullLoggerFactory.Instance;
        _ = services.AddSingleton(factory);

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Same(factory, provider.GetRequiredService<IppTransportOptions>().LoggerFactory);
    }

    [Fact]
    public void AddPrinters_WithNoLoggingRegistered_StillResolves()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetRequiredService<IppTransportOptions>().LoggerFactory);
        _ = Assert.IsType<PrinterManager>(provider.GetRequiredService<IPrinterManager>());
    }

    [Fact]
    public void AddPrinters_KeepsTheLoggerFactoryTheCallerSet()
    {
        ServiceCollection services = new();
        _ = services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        ILoggerFactory chosen = new NullLoggerFactory();

        _ = services.AddPrinters(options => options.LoggerFactory = chosen);

        using var provider = services.BuildServiceProvider();
        Assert.Same(chosen, provider.GetRequiredService<IppTransportOptions>().LoggerFactory);
    }

    [Fact]
    public void AddPrinters_TakesTheLoggerFactoryOfTheContainerForTheManagerToo()
    {
        ServiceCollection services = new();
        ILoggerFactory factory = NullLoggerFactory.Instance;
        _ = services.AddSingleton(factory);

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Same(factory, provider.GetRequiredService<PrinterManagerOptions>().LoggerFactory);
    }

    [Fact]
    public void AddPrinters_GivesEveryPrintingServiceTheSameLoggerFactory()
    {
        // A log that reaches only half of the library answers half of a support case.
        ServiceCollection services = new();
        ILoggerFactory factory = NullLoggerFactory.Instance;
        _ = services.AddSingleton(factory);

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Same(factory, Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>()).LoggerFactory);
        Assert.Same(factory, Assert.IsType<TcpNetworkPrinterDiscovery>(provider.GetRequiredService<INetworkPrinterDiscovery>()).LoggerFactory);
        Assert.Same(factory, Assert.IsType<MdnsPrinterDiscovery>(provider.GetRequiredService<IMdnsPrinterDiscovery>()).LoggerFactory);
        Assert.Same(factory, provider.GetRequiredService<SnmpPrinterStatusClient>().LoggerFactory);
        Assert.Same(factory, Assert.IsType<PrinterFactory>(provider.GetRequiredService<IPrinterFactory>()).LoggerFactory);
        Assert.Same(factory, Assert.IsType<SpoolerPrinterDiscovery>(provider.GetRequiredService<IPrinterDiscovery>()).LoggerFactory);
        Assert.Same(factory, provider.GetRequiredService<SpoolerPrintJobQueue>().LoggerFactory);
        Assert.Same(factory, Assert.IsType<PollingPrintJobMonitor>(provider.GetRequiredService<IPrintJobMonitor>()).LoggerFactory);
    }

    [Fact]
    public void AddPrinters_WithNoLoggingRegistered_LeavesEveryLoggerFactoryNull()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetRequiredService<PrinterManagerOptions>().LoggerFactory);
        Assert.Null(Assert.IsType<TcpPrinterTransport>(provider.GetRequiredService<IPrinterTransport>()).LoggerFactory);
        _ = Assert.IsType<PrinterManager>(provider.GetRequiredService<IPrinterManager>());
    }

    [Fact]
    public void AddPrinters_GivesTheSpoolerTheSameIppPolicy()
    {
        // The CUPS spooler speaks IPP, so the raw-response switch and the log must reach it.
        // It is the channel a stopped spooler job runs on.
        ServiceCollection services = new();

        _ = services.AddPrinters(options => options.CaptureRawResponses = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IppTransportOptions>();
        Assert.Same(options, provider.GetRequiredService<SpoolerPrintJobQueue>().IppTransport);
        Assert.Same(options, Assert.IsType<SpoolerPrinterDiscovery>(provider.GetRequiredService<IPrinterDiscovery>()).IppTransport);
    }

    [Fact]
    public void AddPrinters_ResolvesThePrinterManager()
    {
        ServiceCollection services = new();

        _ = services.AddPrinters();

        using var provider = services.BuildServiceProvider();
        _ = Assert.IsType<PrinterManager>(provider.GetRequiredService<IPrinterManager>());
    }
}
