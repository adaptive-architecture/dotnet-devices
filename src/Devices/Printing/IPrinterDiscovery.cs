namespace AdaptArch.Devices.Printing;

/// <summary>
/// Enumerates printers installed in the operating system print spooler
/// (Win32 print queues, CUPS destinations).
/// </summary>
public interface IPrinterDiscovery
{
    /// <summary>
    /// Lists the printers known to the operating system.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The discovered printers.</returns>
    Task<IReadOnlyList<DiscoveredPrinter>> GetPrintersAsync(CancellationToken cancellationToken);
}
