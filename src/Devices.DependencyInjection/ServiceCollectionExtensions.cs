using AdaptArch.Devices.Printing;
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
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDevices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddPrinters();
    }

    /// <summary>
    /// Registers printer services: transports, network discovery, and the status clients.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPrinters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPrinterTransport, TcpPrinterTransport>();
        services.AddSingleton<INetworkPrinterDiscovery, TcpNetworkPrinterDiscovery>();
        services.AddSingleton<IMdnsPrinterDiscovery, MdnsPrinterDiscovery>();
        services.AddSingleton<IppPrinterStatusClient>();
        services.AddSingleton<SnmpPrinterStatusClient>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPrinterFactory, PrinterFactory>();
        services.AddSingleton<IPrinterDiscovery, SpoolerPrinterDiscovery>();
        services.AddSingleton<SpoolerPrintJobQueue>();
        services.AddSingleton<IPrintJobQueue>(provider => new CompositePrintJobQueue(
            provider.GetRequiredService<SpoolerPrintJobQueue>(),
            CreatePermissiveHttpClient()));
        services.AddSingleton<IPrintJobMonitor, PollingPrintJobMonitor>();
        return services;
    }

    // Matches the certificate policy IppPrinter and PrinterFactory already apply for
    // network printers: without this, a printer reachable over IPPS for printing (a
    // self-signed certificate accepted) would answer only plain IPP for a job read
    // through CompositePrintJobQueue (a default-validating client rejects it).
    private static HttpClient CreatePermissiveHttpClient()
    {
        SocketsHttpHandler handler = new();
        handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        return new HttpClient(handler, true);
    }
}
