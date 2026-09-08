namespace AdaptArch.Devices.Printing;

/// <summary>
/// Discovers printers reachable directly over the network, bypassing the
/// operating system spooler.
/// </summary>
public interface INetworkPrinterDiscovery
{
    /// <summary>
    /// Probes the configured hosts for printers accepting raw print data.
    /// </summary>
    /// <param name="options">The probe scope, timeouts, and parallelism.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The reachable printers.</returns>
    Task<IReadOnlyList<DiscoveredPrinter>> DiscoverNetworkPrintersAsync(NetworkPrinterDiscoveryOptions options, CancellationToken cancellationToken);
}
