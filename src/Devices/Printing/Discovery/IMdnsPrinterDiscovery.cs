namespace AdaptArch.Devices.Printing;

/// <summary>
/// Finds printers that advertise themselves with multicast DNS service discovery,
/// which Apple also calls Bonjour.
/// </summary>
public interface IMdnsPrinterDiscovery
{
    /// <summary>
    /// Asks the local link for printers and collects the answers.
    /// </summary>
    /// <param name="options">The service types, the browse time, and the interfaces to use.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The printers that answered.</returns>
    Task<IReadOnlyList<DiscoveredPrinter>> DiscoverPrintersAsync(MdnsPrinterDiscoveryOptions options, CancellationToken cancellationToken);
}
