namespace AdaptArch.Devices.Printing;

/// <summary>
/// Operational state of a printer.
/// </summary>
public enum PrinterStatusState
{
    /// <summary>
    /// The state is unknown, for example when a raw network printer exposes no status channel.
    /// </summary>
    Unknown,

    /// <summary>
    /// The printer is idle and ready to accept jobs.
    /// </summary>
    Idle,

    /// <summary>
    /// The printer is actively processing a job.
    /// </summary>
    Processing,

    /// <summary>
    /// The printer is paused and not accepting jobs.
    /// </summary>
    Paused,

    /// <summary>
    /// The printer is in an error state. See the status detail for the cause.
    /// </summary>
    Error,

    /// <summary>
    /// The printer is offline or unreachable.
    /// </summary>
    Offline,
}
