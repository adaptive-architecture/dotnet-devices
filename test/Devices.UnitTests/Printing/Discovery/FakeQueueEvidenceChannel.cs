using System.Globalization;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// A channel that answers about its queue from a list held in memory, and records what was
// asked of it. It is also an IPrinter, so PrinterManager's factory can return one.
internal sealed class FakeQueueEvidenceChannel : IPrinter, IQueueEvidenceChannel, IDisposable
{
    private int _nextJobId = 900;

    public FakeQueueEvidenceChannel(DiscoveredPrinter channel)
    {
        Id = channel.Id;
        Endpoint = channel.Endpoint;
        Info = channel.Info;
    }

    public PrinterId Id { get; }

    public PrinterEndpoint Endpoint { get; }

    public PrinterInfo Info { get; }

    // The jobs this channel reports. A tracer created here is added to it, and to the queue
    // of every channel in SharesQueueWith, which is how a test says two channels are one.
    public List<PrinterQueueFingerprint> Queue { get; } = [];

    public List<FakeQueueEvidenceChannel> SharesQueueWith { get; } = [];

    public QueueTracerSupport Support { get; set; } = new(true, true);

    public int Reads { get; private set; }

    public int Creates { get; private set; }

    public List<string> Canceled { get; } = [];

    public List<string> UserNames { get; } = [];

    public bool Disposed { get; private set; }

    public Exception ReadFailure { get; set; }

    public bool ReportCreatedJobId { get; set; } = true;

    public Func<Task> AfterCreate { get; set; }

    Task<IReadOnlyList<PrinterQueueFingerprint>> IQueueEvidenceChannel.ReadQueueAsync(string requestingUserName, CancellationToken cancellationToken)
    {
        Reads++;
        UserNames.Add(requestingUserName);
        return ReadFailure is not null
            ? Task.FromException<IReadOnlyList<PrinterQueueFingerprint>>(ReadFailure)
            : Task.FromResult<IReadOnlyList<PrinterQueueFingerprint>>([.. Queue]);
    }

    Task<QueueTracerSupport> IQueueEvidenceChannel.ReadTracerSupportAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Support);

    async Task<string> IQueueEvidenceChannel.CreateTracerJobAsync(string jobName, string requestingUserName, CancellationToken cancellationToken)
    {
        Creates++;
        UserNames.Add(requestingUserName);
        var jobId = _nextJobId++;
        PrinterQueueFingerprint job = new(jobId, jobName, 42, null, requestingUserName);
        Queue.Add(job);
        foreach (var peer in SharesQueueWith)
        {
            peer.Queue.Add(job);
        }

        if (AfterCreate is not null)
        {
            await AfterCreate().ConfigureAwait(false);
        }

        return ReportCreatedJobId ? jobId.ToString(CultureInfo.InvariantCulture) : null;
    }

    Task IQueueEvidenceChannel.CancelTracerJobAsync(string jobId, string requestingUserName, CancellationToken cancellationToken)
    {
        Canceled.Add(jobId);
        _ = Queue.RemoveAll(job => job.JobId.ToString(CultureInfo.InvariantCulture) == jobId);
        return Task.CompletedTask;
    }

    public Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions options, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PrinterConfiguration(Id));

    public void Dispose() => Disposed = true;
}
