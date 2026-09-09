namespace AdaptArch.Devices.Printing;

/// <summary>
/// Point-in-time operational status of a printer.
/// </summary>
public sealed class PrinterStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterStatus"/> class.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="state">The operational state.</param>
    public PrinterStatus(PrinterId printerId, PrinterStatusState state)
    {
        PrinterId = printerId;
        State = state;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId PrinterId { get; }

    /// <summary>
    /// Gets the operational state.
    /// </summary>
    public PrinterStatusState State { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the printer currently accepts jobs.
    /// </summary>
    public bool IsAcceptingJobs { get; set; } = true;

    /// <summary>
    /// Gets or sets the human-readable detail, such as the reason for an error state.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets the supply markers (ink, toner) reported by the printer. Defaults to empty.
    /// </summary>
    public IReadOnlyList<PrinterMarker> Markers { get; set; } = [];

    /// <summary>
    /// Gets or sets the time at which the status was observed.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the serial number of the printer, or <c>null</c> when the printer did
    /// not report one. Only SNMP fills this today; IPP and the operating system spooler do
    /// not report a serial number yet, but a future path for either could set it.
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of pages that the printer has marked over its life, or
    /// <c>null</c> when the printer did not report one. Only SNMP fills this today; IPP and
    /// the operating system spooler do not report a lifetime page count yet, but a future
    /// path for either could set it.
    /// </summary>
    public long? LifetimePageCount { get; set; }
}
