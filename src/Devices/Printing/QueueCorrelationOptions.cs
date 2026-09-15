namespace AdaptArch.Devices.Printing;

/// <summary>
/// Opt-in policy for proving that two network channels reach one print queue, by comparing
/// the jobs each of them reports.
/// </summary>
/// <remarks>
/// An instance of this class is consent. Reading a queue changes nothing at the printer,
/// but it still opens a session to every candidate channel, so the read is opt-in too.
/// <para>
/// A match proves that two channels address one <em>queue</em>. A class or a pool spreads
/// one queue over several engines, so it is not a statement about sheets of paper.
/// </para>
/// </remarks>
public sealed class QueueCorrelationOptions
{
    /// <summary>
    /// The prefix of the job name every tracer carries, so a job left behind in a queue
    /// names itself to whoever finds it.
    /// </summary>
    public const string TracerJobNamePrefix = "adaptarch-devices-correlation-";

    /// <summary>
    /// Gets or sets a value indicating whether the correlation may create a tracer job on a
    /// channel whose queue held nothing distinctive. Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This is a second and separate consent, because it is what turns a read into a write
    /// on a real printer. Two empty queues prove nothing, and the only way to make them
    /// prove something is to put something in one of them.
    /// <para>
    /// A tracer is a <c>Create-Job</c> with no document at all and
    /// <c>job-hold-until = indefinite</c>, created only on a channel that reported it
    /// supports both, and cancelled in every outcome including a cancelled discovery. A job
    /// that carries no document prints nothing even on a printer that ignores the hold,
    /// which is why the hold is the second guard and not the guard.
    /// </para>
    /// <para>
    /// A tracer is never created while the manager is only refreshing to resolve an
    /// identifier a caller passed to <see cref="IPrinterManager.PrintAsync"/>. Printing one
    /// label must not leave a job on every idle printer on the network.
    /// </para>
    /// </remarks>
    public bool AllowTracerJob { get; set; }

    /// <summary>
    /// Gets or sets the <c>requesting-user-name</c> every request of the correlation
    /// carries. Defaults to <see cref="PrintOptions.DefaultRequestingUserName"/>.
    /// </summary>
    /// <remarks>
    /// A printer may report only the jobs of the user that asked. One name for every channel
    /// keeps that filter identical on both sides of a comparison, so a queue the printer
    /// trimmed is trimmed the same way twice. It is also the name a tracer is created and
    /// cancelled under, which is what an owner-based cancel policy checks.
    /// </remarks>
    public string? RequestingUserName { get; set; }

    /// <summary>
    /// Gets or sets how many channels the correlation may consider. Defaults to sixteen.
    /// </summary>
    /// <remarks>
    /// A discovery with more candidates than this skips the correlation entirely rather than
    /// correlating an arbitrary subset, because a partial answer would depend on which
    /// channels happened to sort first.
    /// </remarks>
    public int MaxChannels { get; set; } = 16;

    /// <summary>
    /// Gets or sets how long the cleanup of a tracer may take. Defaults to five seconds.
    /// </summary>
    /// <remarks>
    /// A tracer is cancelled on a token of its own and never on the caller's, because a
    /// discovery that was cancelled must still take its job back out of the queue.
    /// </remarks>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal string EffectiveUserName =>
        String.IsNullOrWhiteSpace(RequestingUserName) ? PrintOptions.DefaultRequestingUserName : RequestingUserName;
}
