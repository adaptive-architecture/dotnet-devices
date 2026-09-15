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
    /// <remarks>
    /// This caps the whole watch, so it also ends a job that is printing perfectly well and
    /// is simply long. Prefer <see cref="IdleTimeout"/> to tell a slow job from a stuck one,
    /// and set this only as an outer bound.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets how long the watch keeps reading after the last change. The watch ends
    /// quietly when neither the state nor the progress has moved for this long. Defaults to
    /// <c>null</c>, which watches until the job reaches a terminal state.
    /// </summary>
    /// <remarks>
    /// This is the measure that separates a slow job from a stuck one. A printer that wakes
    /// from sleep, warms up for two minutes and then prints steadily trips
    /// <see cref="Timeout"/> for no reason; it never trips this, because every page resets
    /// it. Only a job that has genuinely stopped reporting reaches it.
    /// <para>
    /// Both limits may be set, and whichever comes first ends the watch. Both end it
    /// quietly: the <see cref="CancellationToken"/> the caller passes is the only thing that
    /// throws.
    /// </para>
    /// </remarks>
    public TimeSpan? IdleTimeout { get; set; }
}
