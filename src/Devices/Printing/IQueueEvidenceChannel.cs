namespace AdaptArch.Devices.Printing;

// What a channel must do for its job queue to be used as grouping evidence.
//
// It is a capability and not a part of IPrinter: only a channel with a real job queue can
// answer, and the correlation asks nothing of the ones that cannot. PrinterManager type
// tests the printer its factory returned, so a caller that supplied a factory of its own
// simply gets no correlation rather than an error.
internal interface IQueueEvidenceChannel
{
    // The jobs the channel reports that have not finished, as one user sees them.
    Task<IReadOnlyList<PrinterQueueFingerprint>> ReadQueueAsync(string requestingUserName, CancellationToken cancellationToken);

    // Whether this channel can hold a job that carries no document at all.
    Task<QueueTracerSupport> ReadTracerSupportAsync(CancellationToken cancellationToken);

    // Creates a job with no document, held indefinitely. Returns the identifier the
    // printer assigned, or null when it reported none.
    Task<string?> CreateTracerJobAsync(string jobName, string requestingUserName, CancellationToken cancellationToken);

    // Cancels a job this correlation created. The correlator's cleanup swallows a
    // failure, so this reports one rather than hiding it.
    Task CancelTracerJobAsync(string jobId, string requestingUserName, CancellationToken cancellationToken);
}

// What a channel said about its ability to hold a tracer.
internal readonly record struct QueueTracerSupport(bool CanCreateJob, bool CanHoldIndefinitely)
{
    public bool IsUsable => CanCreateJob && CanHoldIndefinitely;
}
