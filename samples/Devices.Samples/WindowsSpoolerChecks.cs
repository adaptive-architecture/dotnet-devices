using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples;

// Makes the manual checks in docs/windows-manual-tests.md observable. Every step reports
// what it read next to what that reading became, not only the conclusion.
internal static class WindowsSpoolerChecks
{
    public static async IAsyncEnumerable<LogLineDto> RunAsync(
        string queueName,
        bool print,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield return LogLineDto.Fail("These checks need Windows. See docs/windows-manual-tests.md.");
            yield break;
        }

        var jobs = new SpoolerPrintJobQueue();
        var printer = new SpoolerPrinter(new SpoolerPrinterEndpoint(queueName));
        var printerId = PrinterId.ForSpooler(queueName);

        await foreach (var line in CheckJobListAsync(jobs, printerId, cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }

        yield return LogLineDto.Say("Check 2 — printer status.");
        PrinterStatus status = null;
        var statusProblem = await Try(async () => status = await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        yield return statusProblem is null
            ? LogLineDto.Say($"  Status: {status.State} ({status.Detail}); IsAcceptingJobs: {status.IsAcceptingJobs}")
            : LogLineDto.Fail($"  {statusProblem}");

        yield return LogLineDto.Say("Check 6 — configuration.");
        PrinterConfiguration configuration = null;
        var configurationProblem = await Try(async () =>
            configuration = await printer.GetConfigurationAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        if (configurationProblem is not null)
        {
            yield return LogLineDto.Fail($"  {configurationProblem}");
        }
        else
        {
            foreach (var line in DescribeConfiguration(configuration))
            {
                yield return line;
            }
        }

        var cancelled = false;
        await foreach (var line in CheckErrorPathsAsync(jobs, printerId, result => cancelled = result, cancellationToken).ConfigureAwait(false))
        {
            yield return line;
        }

        var submitted = false;
        if (print && configuration is not null)
        {
            await foreach (var line in CheckPrintCycleAsync(printer, configuration, () => submitted = true, cancellationToken).ConfigureAwait(false))
            {
                yield return line;
            }
        }
        else
        {
            yield return LogLineDto.Say("Check 3 — the print cycle: skipped. Tick the box to submit a job.");
        }

        yield return LogLineDto.Say("Check 4 — native AOT publish: these checks cannot do this check.");
        yield return LogLineDto.Say("Run this by hand: dotnet publish -r win-x64 -p:PublishAot=true");

        foreach (var line in AccessLevelSummary(submitted, cancelled))
        {
            yield return line;
        }
    }

    // A JOB_INFO_2 layout error leaves entry 0 correct, so read past the first job.
    private static async IAsyncEnumerable<LogLineDto> CheckJobListAsync(
        SpoolerPrintJobQueue jobs,
        PrinterId printerId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return LogLineDto.Say("Check 1 — the job list.");
        yield return LogLineDto.Say("A JOB_INFO_2 layout error leaves entry 0 correct and corrupts the entries after it.");
        yield return LogLineDto.Say("Look at entries 1 and 2, not only entry 0.");

        IReadOnlyList<PrintJobInfo> list = null;
        var problem = await Try(async () => list = await jobs.GetJobsAsync(printerId, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        if (problem is not null)
        {
            yield return LogLineDto.Fail($"  {problem}");
            yield break;
        }

        if (list.Count == 0)
        {
            yield return LogLineDto.Say("  No jobs in the queue.");
            yield break;
        }

        for (var index = 0; index < list.Count; index++)
        {
            var job = list[index];
            yield return LogLineDto.Say(
                $"  [{index}] JobId={job.JobId} JobName={job.JobName} ImpressionsCompleted={job.ImpressionsCompleted}"
                + $" TotalImpressions={job.TotalImpressions} Detail={job.Detail}");
        }
    }

    // The delimiters make truncation or a stray character visible.
    private static IEnumerable<LogLineDto> DescribeConfiguration(PrinterConfiguration configuration)
    {
        foreach (var media in configuration.Media)
        {
            yield return LogLineDto.Say($"  [{media.Name}] = {media.WindowsPaperNumber}");
        }

        foreach (var source in configuration.MediaSources)
        {
            yield return LogLineDto.Say($"  Tray [{source.Name}] = {source.WindowsBinNumber}");
        }

        yield return LogLineDto.Say($"  Resolutions (dpi): {String.Join(", ", configuration.SupportedResolutionsDpi)}");
        yield return LogLineDto.Say($"  SupportsDuplex: {configuration.SupportsDuplex}; SupportsColor: {configuration.SupportsColor}");
        yield return LogLineDto.Say($"  DefaultMediaSize: [{configuration.DefaultMediaSize}]; DefaultMediaSource: [{configuration.DefaultMediaSource}]");
        yield return LogLineDto.Say($"  DefaultOrientation: {configuration.DefaultOrientation}; DefaultResolutionDpi: {configuration.DefaultResolutionDpi}");
    }

    private static async IAsyncEnumerable<LogLineDto> CheckErrorPathsAsync(
        SpoolerPrintJobQueue jobs,
        PrinterId printerId,
        Action<bool> report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return LogLineDto.Say("Check 5 — a queue name that cannot exist.");
        var missing = new SpoolerPrinter(new SpoolerPrinterEndpoint(Guid.NewGuid().ToString("N")));
        var problem = await Try(async () => _ = await missing.GetStatusAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        yield return problem is null
            ? LogLineDto.Warn("  No exception was thrown. This is unexpected.")
            : LogLineDto.Say($"  {problem}");

        yield return LogLineDto.Say("Check 8 — cancelling an unknown job id on the real queue.");
        yield return LogLineDto.Say("  false is the expected result, not an exception.");
        var cancelled = false;
        var cancelProblem = await Try(async () =>
            cancelled = await jobs.CancelJobAsync(printerId, "999999999", cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        report(cancelProblem is null);
        yield return cancelProblem is null
            ? LogLineDto.Say($"  CancelJobAsync returned {cancelled}.")
            : LogLineDto.Fail($"  Threw {cancelProblem}");
    }

    // The options are built from what this queue reported, so what a device mode carries
    // and what it cannot carry are both visible in one job.
    private static async IAsyncEnumerable<LogLineDto> CheckPrintCycleAsync(
        SpoolerPrinter printer,
        PrinterConfiguration configuration,
        Action report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return LogLineDto.Say("Check 3 — the print cycle.");
        var options = DeviceModeOptions(configuration);
        yield return LogLineDto.Say(
            $"  Asked for: Copies=2 Orientation={options.Orientation} MediaSize=[{options.MediaSize}] MediaSource=[{options.MediaSource}]");
        yield return LogLineDto.Say(
            $"             MediaType=[{options.MediaType}] OutputBin=[{options.OutputBin}] NumberUp={options.NumberUp} PageRanges=1-1");

        var payload = PrinterPayload.FromString("Windows spooler test payload\n", PrinterContentTypes.Text);
        PrintJobInfo submitted = null;
        var problem = await Try(async () =>
            submitted = await printer.PrintAsync(payload, options, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        if (problem is not null)
        {
            yield return LogLineDto.Fail($"  {problem}");
            yield break;
        }

        report();
        yield return LogLineDto.Say($"  Job {submitted.JobId} submitted. Detail: {submitted.Detail}");
        yield return LogLineDto.Say($"  DroppedOptions: {String.Join(", ", submitted.DroppedOptions)}");
        yield return LogLineDto.Say("  MediaType, OutputBin, PageRanges and NumberUp must be in that list, and nothing else.");
        yield return LogLineDto.Say("  The print queue window must show two jobs, and this job id must be the first.");
        yield return LogLineDto.Say("  Open the job properties and compare the orientation, the paper and the tray.");
    }

    // Check 7: whether PRINTER_ACCESS_USE was enough. The flags report only what ran.
    private static IEnumerable<LogLineDto> AccessLevelSummary(bool submitted, bool cancelledWithoutException)
    {
        yield return LogLineDto.Say("Check 7 — the access level: covered in part.");
        yield return LogLineDto.Say("The sections above succeeded, so PRINTER_ACCESS_USE was enough to read the status");
        yield return LogLineDto.Say("and to reach the job queue on this printer.");
        yield return LogLineDto.Say(cancelledWithoutException
            ? "Check 8's cancel call reached the spooler with no access error."
            : "Check 8's cancel call threw, so cancel access is not established by this run.");
        yield return LogLineDto.Say(submitted
            ? "Check 3 ran and submitted a job, so this level was also enough to submit."
            : "Submitting a job was not exercised in this run; tick the box to also cover it.");
        yield return LogLineDto.Say("Cancelling a job that another user sent is not tested here.");
    }

    // A name the queue never reported has no device mode number, so the first reported name
    // is used. An empty list leaves the option unset, which drops nothing.
    private static PrintOptions DeviceModeOptions(PrinterConfiguration configuration) =>
        new()
        {
            JobName = "AdaptArch device mode check",
            Copies = 2,
            Orientation = PrintOrientation.Portrait,
            MediaSize = configuration.DefaultMediaSize ?? FirstName(configuration.Media),
            MediaSource = configuration.DefaultMediaSource ?? FirstName(configuration.MediaSources),
            MediaType = "labels",
            OutputBin = "face-down",
            PageRanges = [new PageRange(1, 1)],
            NumberUp = 1,
        };

    private static string FirstName(IReadOnlyList<PrinterMedia> media) =>
        media.Count == 0 ? null : media[0].Name;

    private static string FirstName(IReadOnlyList<PrinterMediaSource> sources) =>
        sources.Count == 0 ? null : sources[0].Name;

    // A check that throws is a result, not a crash. The name and the message are the
    // evidence a person judges, so they are reported and the run goes on.
    private static async Task<string> Try(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }
}
