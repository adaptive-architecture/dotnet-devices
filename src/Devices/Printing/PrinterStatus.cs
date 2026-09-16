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
    /// Gets a value indicating whether the printer currently accepts jobs.
    /// </summary>
    public bool IsAcceptingJobs { get; init; } = true;

    /// <summary>
    /// Gets the human-readable detail, such as the reason for an error state.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Gets each state reason the printer reported, one for each entry. Defaults to empty.
    /// </summary>
    /// <remarks>
    /// <see cref="Detail"/> holds the same reasons joined into one line. Read this list to
    /// match one reason, for example <c>cups-pki-expired</c>. IPP uses the keyword
    /// <c>none</c> to say that there is no reason at all, so an empty list says the same
    /// thing and the keyword is never an entry.
    /// </remarks>
    public IReadOnlyList<string> StateReasons { get; init; } = [];

    /// <summary>
    /// Gets the readable message the printer reported for its state, or <c>null</c> when it
    /// reported none. This is the IPP <c>printer-state-message</c> attribute. Only the IPP
    /// path fills it.
    /// </summary>
    public string? StateMessage { get; init; }

    /// <summary>
    /// Gets the detailed status messages the printer reported. Defaults to empty. This is
    /// the IPP <c>printer-detailed-status-messages</c> attribute, which is for a person to
    /// read. Do not parse it.
    /// </summary>
    public IReadOnlyList<string> DetailedStatusMessages { get; init; } = [];

    /// <summary>
    /// Gets the transport and the endpoint that answered, or <c>null</c> when the source is
    /// not IPP. A downgrade from IPPS to plain IPP is visible here on a network printer. A
    /// local CUPS queue always reports <see cref="PrinterScheme.Ipp"/>; see
    /// <see cref="PrinterConnection"/>.
    /// </summary>
    public PrinterConnection? Connection { get; init; }

    /// <summary>
    /// Gets the attributes of the raw IPP answer. Empty unless
    /// <see cref="IppTransportOptions.CaptureRawResponses"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<IppAttributeSnapshot> RawAttributes { get; init; } = [];

    /// <summary>
    /// Gets the supply markers (ink, toner) reported by the printer. Defaults to empty.
    /// </summary>
    public IReadOnlyList<PrinterMarker> Markers { get; init; } = [];

    /// <summary>
    /// Gets the time at which the status was observed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Gets the serial number of the printer, or <c>null</c> when the printer did
    /// not report one. Only SNMP fills this today; IPP and the operating system spooler do
    /// not report a serial number yet, but a future path for either could set it.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets the number of pages that the printer has marked over its life, or
    /// <c>null</c> when the printer did not report one. Only SNMP fills this today; IPP and
    /// the operating system spooler do not report a lifetime page count yet, but a future
    /// path for either could set it.
    /// </summary>
    public long? LifetimePageCount { get; init; }
}
