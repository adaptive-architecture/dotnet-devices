using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

        // First: every registration below reads the log factory from it. Registered as well
        // so that the manager constructor that takes it is the one resolved.
        services.TryAddSingleton(provider =>
        {
            PrinterManagerOptions options = new();
            configureManager?.Invoke(options);

            // The container decides the log, unless the caller set one of its own. GetService,
            // not GetRequiredService: an application without logging must still resolve.
            options.LoggerFactory ??= provider.GetService<ILoggerFactory>();
            return options;
        });

        // Each of these is built by a factory, not by its type, so the log factory reaches
        // the init property that carries it.
        services.TryAddSingleton<IPrinterTransport>(provider => new TcpPrinterTransport
        {
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton<INetworkPrinterDiscovery>(provider => new TcpNetworkPrinterDiscovery
        {
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton<IMdnsPrinterDiscovery>(provider => new MdnsPrinterDiscovery
        {
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton(provider => new SnmpPrinterStatusClient
        {
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton(TimeProvider.System);

        // One client for every network printer, so they share a connection pool and a
        // certificate policy.
        services.TryAddSingleton(provider =>
        {
            IppTransportOptions options = new();
            configure?.Invoke(options);

            // The container decides the log, unless the caller set one of its own. GetService,
            // not GetRequiredService: an application without logging must still resolve.
            options.LoggerFactory ??= provider.GetService<ILoggerFactory>();
            return options;
        });
        services.TryAddSingleton(provider => new IppHttpClientHolder(IppHttpClientFactory.Create(provider.GetRequiredService<IppTransportOptions>())));
        services.TryAddSingleton(provider => new IppPrinterStatusClient(
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>()));
        services.TryAddSingleton<IPrinterFactory>(provider => new PrinterFactory(
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>())
        {
            // The manager options carry the formats and the converters, so a printer the
            // factory opens reads the same policy as the manager that asked for it.
            Formats = provider.GetRequiredService<PrinterManagerOptions>().BuildFormatPolicy(),
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton<IPrinterDiscovery>(provider => new SpoolerPrinterDiscovery
        {
            IppTransport = provider.GetRequiredService<IppTransportOptions>(),
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton(provider => new SpoolerPrintJobQueue
        {
            IppTransport = provider.GetRequiredService<IppTransportOptions>(),
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton<IPrintJobQueue>(provider => new CompositePrintJobQueue(
            provider.GetRequiredService<SpoolerPrintJobQueue>(),
            provider.GetRequiredService<IppHttpClientHolder>().Client,
            provider.GetRequiredService<IppTransportOptions>()));
        services.TryAddSingleton<IPrintJobMonitor>(provider => new PollingPrintJobMonitor(
            provider.GetRequiredService<IPrintJobQueue>(),
            provider.GetRequiredService<TimeProvider>())
        {
            LoggerFactory = Log(provider),
        });
        services.TryAddSingleton<IPrinterManager, PrinterManager>();
        return services;
    }

    // The log factory the manager options settled on, so every registration reads one answer.
    private static ILoggerFactory? Log(IServiceProvider provider) =>
        provider.GetRequiredService<PrinterManagerOptions>().LoggerFactory;

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
