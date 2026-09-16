using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// What became of one job. A set reports these at the end, because a set that prints five
// documents scrolls the early ones off the panel.
internal sealed class JobOutcome
{
    internal const string Printed = "Printed";
    internal const string StillPrinting = "Still printing when the watch ended";
    internal const string Skipped = "Skipped";
    internal const string Failed = "Failed";

    public string What { get; set; }

    public string Result { get; set; } = Failed;

    public string Detail { get; set; }
}

// Submits a job and reports what became of it, one line at a time. The page shows those
// lines as they arrive, which is what the console printed as it went.
internal sealed class PrintJobRunner
{
    private readonly IPrinterManager _manager;
    private readonly IPrintJobMonitor _monitor;
    private readonly PrinterCatalog _catalog;
    private readonly SamplePaths _paths;

    public PrintJobRunner(IPrinterManager manager, IPrintJobMonitor monitor, PrinterCatalog catalog, SamplePaths paths)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(paths);
        _manager = manager;
        _monitor = monitor;
        _catalog = catalog;
        _paths = paths;
    }

    // One job: submit it, then watch it to a terminal state. Never throws. A job that fails
    // must not stop the jobs after it in a set.
    public async IAsyncEnumerable<LogLineDto> RunAsync(
        PrinterId printerId,
        byte[] bytes,
        string contentType,
        PrintOptions options,
        bool raw,
        JobOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var accepts = _catalog.Accepts(printerId, contentType);

        // A raw send is not converted, so a format the channel says it does not read would
        // only waste paper. A queue job is still sent: the page warned before the button.
        if (raw && accepts.Accepts == false)
        {
            outcome.Result = JobOutcome.Skipped;
            outcome.Detail = $"the channel does not read {contentType}";
            yield return LogLineDto.Warn($"Skipped: this channel does not read {contentType}.");
            if (accepts.Reads is not null)
            {
                yield return LogLineDto.Warn($"This channel reads: {accepts.Reads}");
            }

            yield break;
        }

        if (accepts.Accepts == false)
        {
            yield return LogLineDto.Warn($"{printerId} does not report {contentType}. The page may come out blank.");
            if (accepts.Reads is not null)
            {
                yield return LogLineDto.Warn($"This channel reads: {accepts.Reads}");
            }
        }
        else if (accepts.Accepts is null)
        {
            yield return LogLineDto.Warn($"{printerId} reports no format, so it is not known whether it reads {contentType}.");
        }

        // This budget covers handing the job over, and nothing else. It deliberately does
        // not cover the watch: a printer that is still printing has not failed, and a
        // deadline is not a cancellation.
        using var submitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        submitSource.CancelAfter(TimeSpan.FromMinutes(2));

        PrintJobInfo submitted = null;
        string failure = null;
        var skipped = false;
        try
        {
            submitted = await _manager.PrintAsync(
                printerId,
                PrinterPayload.FromBytes(bytes, contentType),
                options,
                submitSource.Token).ConfigureAwait(false);
        }
        catch (NotSupportedException exception)
        {
            // The refusal is the check working: every channel reported other formats only.
            skipped = true;
            failure = $"Nothing was sent, because this printer does not read the format. {exception.Message}";
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException)
        {
            failure = $"Nothing was sent: {exception.Message}";
        }
        catch (OperationCanceledException)
        {
            failure = "Nothing was sent: the printer did not take the job in two minutes.";
        }

        if (failure is not null)
        {
            outcome.Result = skipped ? JobOutcome.Skipped : JobOutcome.Failed;
            outcome.Detail = failure;
            yield return LogLineDto.Fail(failure);
            yield break;
        }

        yield return LogLineDto.Say($"Job {submitted.JobId} submitted ({submitted.State}).");
        if (submitted.DroppedOptions.Count > 0)
        {
            yield return LogLineDto.Warn($"Dropped options: {String.Join(", ", submitted.DroppedOptions)}");
        }

        if (raw)
        {
            yield return LogLineDto.Say("A raw send carries no job template. Look at the printer to see the result.");
        }

        if (submitted.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
        {
            yield return Final(submitted, outcome);
            yield break;
        }

        // A channel with no job queue reports the job done as soon as it took the bytes.
        if (!_catalog.HasJobQueue(printerId))
        {
            outcome.Result = JobOutcome.Printed;
            outcome.Detail = $"job {submitted.JobId} sent";
            yield return LogLineDto.Say("This channel has no job queue, so there is no progress to watch.");
            yield break;
        }

        await foreach (var line in WatchAsync(printerId, submitted, outcome, cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    // The watch is bounded by lost progress, not by a wall clock. A printer that woke from
    // sleep may take minutes over the first page and then print steadily; every page resets
    // the idle timeout, so only a job that has really stopped ends the watch.
    private async IAsyncEnumerable<LogLineDto> WatchAsync(
        PrinterId printerId,
        PrintJobInfo submitted,
        JobOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        PrintJobMonitorOptions watch = new() { IdleTimeout = TimeSpan.FromMinutes(2) };
        var last = submitted;
        outcome.Result = JobOutcome.StillPrinting;
        outcome.Detail = $"last seen {last.State}";

        var readings = _monitor.WatchJobAsync(printerId, submitted.JobId, watch, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                string stopped = null;
                try
                {
                    if (!await readings.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    last = readings.Current;
                }
                catch (OperationCanceledException)
                {
                    // The browser closed the page. The printer keeps the job.
                    yield break;
                }
                catch (Exception exception)
                {
                    // Losing sight of a job does not undo it: the printer may well finish.
                    stopped = exception.Message;
                }

                if (stopped is not null)
                {
                    outcome.Detail = stopped;
                    yield return LogLineDto.Warn($"The watch stopped: {stopped}");
                    yield break;
                }

                yield return LogLineDto.Say(SampleHelpers.DescribeJobReading(last));
            }
        }
        finally
        {
            await readings.DisposeAsync().ConfigureAwait(false);
        }

        if (last.State is not (PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled))
        {
            outcome.Detail = $"last seen {last.State}";
            yield return LogLineDto.Warn("Nothing changed for two minutes; the job may still be printing.");
            yield break;
        }

        yield return Final(last, outcome);
    }

    private static LogLineDto Final(PrintJobInfo job, JobOutcome outcome)
    {
        switch (job.State)
        {
            case PrintJobState.Completed:
                outcome.Result = JobOutcome.Printed;
                outcome.Detail = $"job {job.JobId}";
                return new LogLineDto($"Job {job.JobId} printed.", LogLineDto.Done);
            case PrintJobState.Failed:
                outcome.Result = JobOutcome.Failed;
                outcome.Detail = job.Detail ?? "the printer reported a failure";
                return LogLineDto.Fail(outcome.Detail);
            case PrintJobState.Canceled:
                outcome.Result = JobOutcome.Failed;
                outcome.Detail = "the job was cancelled";
                return LogLineDto.Fail("The job was cancelled.");
            default:
                outcome.Result = JobOutcome.StillPrinting;
                outcome.Detail = $"last seen {job.State}";
                return LogLineDto.Say($"Last seen {job.State}.");
        }
    }

    // A fixed sequence, in a fixed order, so two runs can be compared and a change in the
    // output has one cause.
    public async IAsyncEnumerable<LogLineDto> RunSetAsync(
        PrinterId printerId,
        JobSetDto set,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var raw = set.Mode == JobSetDto.RawMode;
        yield return LogLineDto.Say($"== {set.Name ?? "Job set"} — {set.Jobs.Count} job(s) to {printerId} ==");
        if (set.Description is not null)
        {
            yield return LogLineDto.Say(set.Description);
        }

        List<JobOutcome> outcomes = [];
        foreach (var job in set.Jobs)
        {
            JobOutcome outcome = new() { What = job.File };
            outcomes.Add(outcome);
            yield return LogLineDto.Say($"-- {job.File}: {job.Description ?? "no description"}");

            var prepared = Prepare(job, outcome);
            if (prepared.Problem is not null)
            {
                yield return LogLineDto.Fail(prepared.Problem);
                continue;
            }

            await foreach (var line in RunAsync(
                printerId, prepared.Bytes, prepared.ContentType, prepared.Options, raw, outcome, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return line;
            }
        }

        foreach (var line in Summary(outcomes))
        {
            yield return line;
        }
    }

    private readonly record struct PreparedJob(byte[] Bytes, string ContentType, PrintOptions Options, string Problem);

    // Reading the file and reading the options both fail in ways the person can fix, so
    // each failure names the file and the reason.
    private PreparedJob Prepare(JobDefinitionDto job, JobOutcome outcome)
    {
        var path = SampleHelpers.SafePath(_paths.PrintFiles, job.File);
        if (path is null || !File.Exists(path))
        {
            outcome.Result = JobOutcome.Skipped;
            outcome.Detail = "the file is not in PrintFiles";
            return new PreparedJob(null, null, null, $"'{job.File}' is not in PrintFiles, so this job is skipped.");
        }

        string contentType;
        PrintOptions options;
        try
        {
            contentType = job.ContentType ?? SampleHelpers.GetContentType(job.File);
            options = (job.Options ?? new PrintOptionsDto()).ToOptions($"{job.File} — {job.Description}");
        }
        catch (Exception exception) when (exception is NotSupportedException or FormatException)
        {
            outcome.Result = JobOutcome.Skipped;
            outcome.Detail = exception.Message;
            return new PreparedJob(null, null, null, exception.Message);
        }

        return new PreparedJob(File.ReadAllBytes(path), contentType, options, null);
    }

    private static IEnumerable<LogLineDto> Summary(List<JobOutcome> outcomes)
    {
        yield return LogLineDto.Say("== Summary ==");
        foreach (var group in new[] { JobOutcome.Printed, JobOutcome.StillPrinting, JobOutcome.Failed, JobOutcome.Skipped })
        {
            var matching = outcomes.FindAll(outcome => outcome.Result == group);
            if (matching.Count == 0)
            {
                continue;
            }

            yield return LogLineDto.Say($"{group} ({matching.Count}):");
            foreach (var outcome in matching)
            {
                yield return new LogLineDto($"  {outcome.What} — {outcome.Detail}", Level(group));
            }
        }

        var unfinished = outcomes.FindAll(static outcome => outcome.Result == JobOutcome.StillPrinting).Count;
        if (unfinished > 0)
        {
            // Not a failure. The watch gave up on the job; the printer did not.
            yield return LogLineDto.Warn(
                $"{unfinished} job(s) were still going when the watch ended. Look at the printer for the result.");
        }
    }

    private static string Level(string result) => result switch
    {
        JobOutcome.Printed => LogLineDto.Done,
        JobOutcome.Failed => LogLineDto.Error,
        JobOutcome.Skipped => LogLineDto.Warning,
        _ => LogLineDto.Warning,
    };
}
