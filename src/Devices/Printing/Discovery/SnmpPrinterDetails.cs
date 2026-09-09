namespace AdaptArch.Devices.Printing;

/// <summary>
/// Identity and status details of a network printer read over SNMP. It carries the same
/// pair as <see cref="IppPrinterDetails"/>: the serial number and the lifetime page count
/// live on <see cref="PrinterStatus"/> itself.
/// </summary>
public sealed class SnmpPrinterDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpPrinterDetails"/> class.
    /// </summary>
    /// <param name="info">Descriptive information about the printer.</param>
    /// <param name="status">The operational status of the printer.</param>
    public SnmpPrinterDetails(PrinterInfo info, PrinterStatus status)
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
    /// Gets the operational status of the printer, including the serial number, the
    /// lifetime page count, and supply markers when reported.
    /// </summary>
    public PrinterStatus Status { get; }
}
