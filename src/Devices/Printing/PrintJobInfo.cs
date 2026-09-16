namespace AdaptArch.Devices.Printing;

/// <summary>
/// Describes a print job submitted to a printer.
/// </summary>
public sealed class PrintJobInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrintJobInfo"/> class.
    /// </summary>
    /// <param name="jobId">The job identifier assigned by the printer or spooler.</param>
    /// <param name="printerId">The printer identifier.</param>
    /// <param name="state">The job state.</param>
    public PrintJobInfo(string jobId, PrinterId printerId, PrintJobState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        JobId = jobId;
        PrinterId = printerId;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the job identifier assigned by the printer or spooler.
    /// </summary>
    public string JobId { get; }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId PrinterId { get; }

    /// <summary>
    /// Gets or sets the job state.
    /// </summary>
    public PrintJobState State { get; set; }

    /// <summary>
    /// Gets or sets the human-readable job name, when known.
    /// </summary>
    public string? JobName { get; set; }

    /// <summary>
    /// Gets or sets the time at which the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time at which the job reached a terminal state, when applicable.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the number of pages printed, when the source reports it.
    /// </summary>
    public int? ImpressionsCompleted { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages in the job, when the source reports it.
    /// </summary>
    public int? TotalImpressions { get; set; }

    /// <summary>
    /// Gets or sets a readable reason for the current state, when the source reports one.
    /// </summary>
    public string? Detail { get; set; }

    /// <summary>
    /// Gets or sets each state reason the source reported, one for each entry. Defaults to
    /// empty.
    /// </summary>
    /// <remarks>
    /// <see cref="Detail"/> holds the same reasons joined into one line. Read this list to
    /// match one reason, for example <c>resources-are-not-ready</c>. IPP uses the keyword
    /// <c>none</c> to say that there is no reason at all, so an empty list says the same
    /// thing and the keyword is never an entry.
    /// </remarks>
    public IReadOnlyList<string> StateReasons { get; set; } = [];

    /// <summary>
    /// Gets or sets the readable message the source reported for the job state, or
    /// <c>null</c> when it reported none. This is the IPP <c>job-state-message</c> attribute.
    /// </summary>
    public string? StateMessage { get; set; }

    /// <summary>
    /// Gets or sets the readable message the printer reported while it held this job, or
    /// <c>null</c> when it reported none. This is the IPP <c>job-printer-state-message</c>
    /// attribute, which CUPS fills with the text of its own log. It often names the cause
    /// that the job state reasons cannot say.
    /// </summary>
    public string? PrinterStateMessage { get; set; }

    /// <summary>
    /// Gets or sets the detailed status messages the source reported. Defaults to empty.
    /// This is the IPP <c>job-detailed-status-messages</c> attribute, which is for a person
    /// to read. Do not parse it.
    /// </summary>
    public IReadOnlyList<string> DetailedStatusMessages { get; set; } = [];

    /// <summary>
    /// Gets or sets the attributes of the raw IPP answer. Empty unless
    /// <see cref="IppTransportOptions.CaptureRawResponses"/> is <c>true</c>. A read of one
    /// job fills it; a read of the whole queue does not, so one answer is not copied into
    /// every job.
    /// </summary>
    public IReadOnlyList<IppAttributeSnapshot> RawAttributes { get; set; } = [];

    /// <summary>
    /// Gets or sets the options that did not reach the device. An option is listed when
    /// <see cref="UnsupportedOptionBehavior.Drop"/> removed it, or when the channel cannot
    /// apply it at all, as the Windows spooler does for an option that no device mode
    /// field can carry.
    /// </summary>
    public IReadOnlyList<string> DroppedOptions { get; set; } = [];
}
