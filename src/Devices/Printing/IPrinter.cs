namespace AdaptArch.Devices.Printing;

/// <summary>
/// Abstraction over a single printer. Implementations are testable seams:
/// mock this interface in unit tests instead of touching hardware.
/// </summary>
public interface IPrinter
{
    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    PrinterId Id { get; }

    /// <summary>
    /// Gets the endpoint the printer is reachable at.
    /// </summary>
    PrinterEndpoint Endpoint { get; }

    /// <summary>
    /// Gets descriptive information about the printer.
    /// </summary>
    PrinterInfo Info { get; }

    /// <summary>
    /// Submits a raw payload for printing.
    /// </summary>
    /// <param name="payload">The raw bytes and content type to print.</param>
    /// <param name="options">Optional per-job printing options. Unset properties fall back to printer defaults.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The submitted job. Transports without job tracking return a completed job with a generated identifier.</returns>
    Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken);

    /// <summary>
    /// Queries the current operational status of the printer.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The current status. Printers without a status channel report <see cref="PrinterStatusState.Unknown"/>.</returns>
    Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Queries the capabilities and configuration of the printer.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The printer configuration.</returns>
    Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken);
}
