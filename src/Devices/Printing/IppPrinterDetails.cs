namespace AdaptArch.Devices.Printing;

/// <summary>
/// Identity and status details of a network printer read over IPP.
/// </summary>
public sealed class IppPrinterDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IppPrinterDetails"/> class.
    /// </summary>
    /// <param name="info">Descriptive information about the printer.</param>
    /// <param name="status">The operational status of the printer.</param>
    public IppPrinterDetails(PrinterInfo info, PrinterStatus status)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(status);
        Info = info;
        Status = status;
    }

    /// <summary>
    /// Gets descriptive information about the printer.
    /// </summary>
    public PrinterInfo Info { get; }

    /// <summary>
    /// Gets the operational status of the printer, including supply markers when reported.
    /// </summary>
    public PrinterStatus Status { get; }
}
