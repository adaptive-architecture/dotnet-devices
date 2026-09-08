using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;

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
    /// Registers printer services: transports and network discovery.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPrinters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPrinterTransport, TcpPrinterTransport>();
        services.AddSingleton<INetworkPrinterDiscovery, TcpNetworkPrinterDiscovery>();
        return services;
    }
}
