namespace AdaptArch.Devices.Printing;

/// <summary>
/// Describes where a printer is reachable. Transport mechanics stay inside
/// <see cref="IPrinterTransport"/> implementations; endpoints are pure data.
/// </summary>
public abstract class PrinterEndpoint
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterEndpoint"/> class.
    /// </summary>
    protected PrinterEndpoint()
    {
    }

    /// <summary>
    /// Gets the transport channel this endpoint is reached over.
    /// </summary>
    public abstract PrinterScheme Scheme { get; }
}
