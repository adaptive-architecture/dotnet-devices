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
}
