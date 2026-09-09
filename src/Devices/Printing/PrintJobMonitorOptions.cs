namespace AdaptArch.Devices.Printing;

/// <summary>
/// Options for <see cref="IPrintJobMonitor.WatchJobAsync"/>.
/// </summary>
public sealed class PrintJobMonitorOptions
{
    /// <summary>
    /// Gets or sets the wait time between reads of the job queue. Defaults to one second.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the maximum time to watch the job before the watch ends quietly.
    /// Defaults to <c>null</c>, which watches until the job reaches a terminal state.
    /// </summary>
    public TimeSpan? Timeout { get; set; }
}
