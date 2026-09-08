namespace AdaptArch.Devices.Printing;

/// <summary>
/// A printer found by discovery, pairing its identity and endpoint with descriptive info.
/// </summary>
public sealed class DiscoveredPrinter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DiscoveredPrinter"/> class.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="endpoint">The endpoint the printer is reachable at.</param>
    /// <param name="info">Descriptive information about the printer.</param>
    public DiscoveredPrinter(PrinterId id, PrinterEndpoint endpoint, PrinterInfo info)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(info);
        Id = id;
        Endpoint = endpoint;
        Info = info;
    }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId Id { get; }

    /// <summary>
    /// Gets the endpoint the printer is reachable at.
    /// </summary>
    public PrinterEndpoint Endpoint { get; }

    /// <summary>
    /// Gets descriptive information about the printer.
    /// </summary>
    public PrinterInfo Info { get; }
}
