namespace AdaptArch.Devices.Printing;

/// <summary>
/// Low-level transmission channel for raw printer payloads. Endpoints describe
/// <i>where</i> a printer is; transports implement <i>how</i> bytes get there
/// (TCP, OS spooler).
/// </summary>
public interface IPrinterTransport
{
    /// <summary>
    /// Determines whether this transport can transmit to the given endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint to check.</param>
    /// <returns><c>true</c> when this transport supports the endpoint; otherwise <c>false</c>.</returns>
    bool CanHandle(PrinterEndpoint endpoint);

    /// <summary>
    /// Transmits a raw payload to the printer at the given endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint to transmit to.</param>
    /// <param name="payload">The raw bytes and content type to transmit.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A task representing the transmission.</returns>
    /// <exception cref="NotSupportedException">Thrown when the endpoint is not supported by this transport.</exception>
    Task WriteAsync(PrinterEndpoint endpoint, PrinterPayload payload, CancellationToken cancellationToken);
}
