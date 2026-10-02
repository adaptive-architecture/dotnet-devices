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
    /// <remarks>
    /// A printer the default factory returns owns no connection of its own: it shares the
    /// clients of the factory, so the caller disposes the factory and not the printers.
    /// Another implementation may hand out printers that implement <see cref="IDisposable"/>;
    /// the caller disposes those.
    /// </remarks>
    IPrinter Open(DiscoveredPrinter printer);

    /// <summary>
    /// Opens a printer by its identifier. The scheme of the identifier names the transport,
    /// so nothing is probed.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A printer for the identifier.</returns>
    /// <exception cref="NotSupportedException">Thrown when the identifier kind is not supported yet.</exception>
    Task<IPrinter> OpenAsync(PrinterId id, CancellationToken cancellationToken);
}
