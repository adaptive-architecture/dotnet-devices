namespace AdaptArch.Devices.Printing;

/// <summary>
/// Identity and status details of a network printer read over SNMP. It carries the same
/// pair as <see cref="IppPrinterDetails"/>, and two fields that the shared models have no
/// place for.
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
    /// Gets the operational status of the printer, including supply markers when reported.
    /// </summary>
    public PrinterStatus Status { get; }

    /// <summary>
    /// Gets or sets the serial number of the printer, or <c>null</c> when it is not reported.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of pages that the printer has marked over its life, or
    /// <c>null</c> when it is not reported.
    /// </summary>
    public long? LifetimePageCount { get; set; }
}
