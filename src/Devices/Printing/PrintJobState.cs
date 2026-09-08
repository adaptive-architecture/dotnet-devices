namespace AdaptArch.Devices.Printing;

/// <summary>
/// Lifecycle state of a print job.
/// </summary>
public enum PrintJobState
{
    /// <summary>
    /// The job is queued and waiting to print.
    /// </summary>
    Queued,

    /// <summary>
    /// The job is currently printing.
    /// </summary>
    Printing,

    /// <summary>
    /// The job is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// The job completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The job failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The job was canceled.
    /// </summary>
    Canceled,
}
