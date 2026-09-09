namespace AdaptArch.Devices.Printing;

/// <summary>
/// Turns a discovered printer, or a bare printer identifier, into a ready
/// <see cref="IPrinter"/> for the transport that fits its endpoint.
/// </summary>
public interface IPrinterFactory
{
    /// <summary>
    /// Opens a printer found by discovery.
    /// </summary>
    /// <param name="printer">The discovered printer.</param>
    /// <returns>A printer for the discovered endpoint.</returns>
    /// <exception cref="NotSupportedException">Thrown when the endpoint is not supported yet.</exception>
    IPrinter Open(DiscoveredPrinter printer);

    /// <summary>
    /// Opens a printer by its identifier, probing to pick the right transport.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A printer for the identifier.</returns>
    /// <exception cref="NotSupportedException">Thrown when the identifier kind is not supported yet.</exception>
    Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken);
}
