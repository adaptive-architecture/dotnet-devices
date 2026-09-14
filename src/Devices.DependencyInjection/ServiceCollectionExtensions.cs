using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AdaptArch.Devices.DependencyInjection;

/// <summary>
/// Dependency injection registrations for <c>AdaptArch.Devices</c>.
/// Kept in this package so the core library ships with zero runtime dependencies.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all device services, including printers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback that sets the IPP transport policy: the certificate trust, the plain IPP fallback, and the connect timeout.</param>
    /// <param name="configureManager">An optional callback that sets the printer manager policy: the transports it may open, and the discovery scope it uses by default.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDevices(
        this IServiceCollection services,
        Action<IppTransportOptions>? configure = null,
        Action<PrinterManagerOptions>? configureManager = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddPrinters(configure, configureManager);
    }

    /// <summary>
    /// Registers printer services: transports, network discovery, the status clients, the
    /// printer factory, the job queue, the job monitor, and the printer manager. Every
    /// registration uses <c>TryAdd</c>, so a second call, or a registration the application
    /// made first, is left in place.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback that sets the IPP transport policy: the certificate trust, the plain IPP fallback, and the connect timeout.</param>
    /// <param name="configureManager">
    /// An optional callback that sets the printer manager policy. Set
    /// <see cref="PrinterManagerOptions.Transports"/> to limit the manager to some
    /// transports, for example <c>[PrinterScheme.Spooler]</c> to print through the
    /// operating system only. Discovery still finds every channel.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPrinters(
        this IServiceCollection services,
        Action<IppTransportOptions>? configure = null,
        Action<PrinterManagerOptions>? configureManager = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IPrinterTransport, TcpPrinterTransport>();
        services.TryAddSingleton<INetworkPrinterDiscovery, TcpNetworkPrinterDiscovery>();
        services.TryAddSingleton<IMdnsPrinterDiscovery, MdnsPrinterDiscovery>();
        services.TryAddSingleton<SnmpPrinterStatusClient>();
        services.TryAddSingleton(TimeProvider.System);

        // One client for every network printer, so they share a connection pool and a
        // certificate policy.
        services.TryAddSingleton(_ =>
        {
            IppTransportOptions options = new();
            configure?.Invoke(options);
            return options;
        });
        services.TryAddSingleton(provider => new IppHttpClientHolder(IppHttpClientFactory.Create(provider.GetRequiredService<IppTransportOptions>())));
        services.TryAddSingleton(provider => new IppPrinterStatusClient(
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>()));
        services.TryAddSingleton<IPrinterFactory>(provider => new PrinterFactory(
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>()));
        services.TryAddSingleton<IPrinterDiscovery, SpoolerPrinterDiscovery>();
        services.TryAddSingleton<SpoolerPrintJobQueue>();
        services.TryAddSingleton<IPrintJobQueue>(provider => new CompositePrintJobQueue(
            provider.GetRequiredService<SpoolerPrintJobQueue>(),
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>()));
        services.TryAddSingleton<IPrintJobMonitor, PollingPrintJobMonitor>();

        // Registered, so the manager constructor that takes it is the one resolved.
        services.TryAddSingleton(_ =>
        {
            PrinterManagerOptions options = new();
            configureManager?.Invoke(options);
            return options;
        });
        services.TryAddSingleton<IPrinterManager, PrinterManager>();
        return services;
    }

    // Owns the shared HttpClient. It is not registered directly, because an application
    // may register its own.
    internal sealed class IppHttpClientHolder : IDisposable
    {
        public IppHttpClientHolder(HttpClient client)
        {
            Client = client;
        }

        public HttpClient Client { get; }

        public void Dispose() => Client.Dispose();
    }
}
