using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Samples;

// Makes the manual checks in docs/windows-manual-tests.md observable. Every step prints
// what it read next to what that reading became, not only the conclusion.
internal static class WindowsPrinterTestScenario
{
    internal static async Task RunAsync(string queueName, bool print)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("This scenario needs Windows. See docs/windows-manual-tests.md.");
            return;
        }

        var jobs = new SpoolerPrintJobQueue();
        var printer = new SpoolerPrinter(new SpoolerPrinterEndpoint(queueName));
        var printerId = PrinterId.ForSpooler(queueName);

        await CheckJobListAsync(jobs, printerId).ConfigureAwait(false);
        await CheckPrinterStatusAsync(printer).ConfigureAwait(false);
        var configuration = await CheckConfigurationAsync(printer).ConfigureAwait(false);
        var cancelledWithoutException = await CheckErrorPathsAsync(jobs, printerId).ConfigureAwait(false);

        var submitted = false;
        if (print)
        {
            submitted = await CheckPrintCycleAsync(printer, configuration).ConfigureAwait(false);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Check 3 — the print cycle: skipped. Pass --print to submit a job.");
        }

        Console.WriteLine();
        Console.WriteLine("Check 4 — native AOT publish: this scenario cannot do this check.");
        Console.WriteLine("Run this by hand: dotnet publish -r win-x64 -p:PublishAot=true");

        Console.WriteLine();
        PrintAccessLevelSummary(submitted, cancelledWithoutException);
    }

    // Check 7: whether PRINTER_ACCESS_USE was enough. The flags report only what ran.
    private static void PrintAccessLevelSummary(bool submitted, bool cancelledWithoutException)
    {
        Console.WriteLine("Check 7 — the access level: covered in part.");
        Console.WriteLine("The sections above succeeded, so PRINTER_ACCESS_USE was enough to read the status");
        Console.WriteLine("and to reach the job queue on this printer.");
        Console.WriteLine(cancelledWithoutException
            ? "Check 8's cancel call reached the spooler with no access error."
            : "Check 8's cancel call threw, so cancel access is not established by this run.");
        Console.WriteLine(submitted
            ? "Check 3 ran and submitted a job, so this level was also enough to submit."
            : "Submitting a job was not exercised in this run; pass --print to also cover it.");
        Console.WriteLine("Cancelling a job that another user sent is not tested here.");
    }

    // A JOB_INFO_2 layout error leaves entry 0 correct, so read past the first job.
    private static async Task CheckJobListAsync(SpoolerPrintJobQueue jobs, PrinterId printerId)
    {
        Console.WriteLine("Check 1 — the job list.");
        Console.WriteLine("A JOB_INFO_2 layout error leaves entry 0 correct and corrupts the entries after it.");
        Console.WriteLine("Look at entries 1 and 2, not only entry 0.");

        var list = await jobs.GetJobsAsync(printerId, CancellationToken.None).ConfigureAwait(false);
        if (list.Count == 0)
        {
            Console.WriteLine("  No jobs in the queue.");
            return;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var job = list[i];
            Console.WriteLine($"  [{i}] JobId={job.JobId} JobName={job.JobName} ImpressionsCompleted={job.ImpressionsCompleted} TotalImpressions={job.TotalImpressions} Detail={job.Detail}");
        }
    }

    private static async Task CheckPrinterStatusAsync(SpoolerPrinter printer)
    {
        Console.WriteLine();
        Console.WriteLine("Check 2 — printer status.");
        var status = await printer.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"  Status: {status.State}  ({status.Detail})");
        Console.WriteLine($"  IsAcceptingJobs: {status.IsAcceptingJobs}");
    }

    private static async Task<PrinterConfiguration> CheckConfigurationAsync(SpoolerPrinter printer)
    {
        Console.WriteLine();
        Console.WriteLine("Check 6 — configuration.");
        var configuration = await printer.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var media in configuration.Media)
        {
            // The delimiters make truncation or a stray character visible.
            Console.WriteLine($"  [{media.Name}] = {media.WindowsPaperNumber}");
        }

        foreach (var source in configuration.MediaSources)
        {
            Console.WriteLine($"  Tray [{source.Name}] = {source.WindowsBinNumber}");
        }

        Console.WriteLine($"  Resolutions (dpi): {String.Join(", ", configuration.SupportedResolutionsDpi)}");
        Console.WriteLine($"  SupportsDuplex: {configuration.SupportsDuplex}");
        Console.WriteLine($"  SupportsColor: {configuration.SupportsColor}");
        Console.WriteLine($"  DefaultMediaSize: [{configuration.DefaultMediaSize}]");
        Console.WriteLine($"  DefaultMediaSource: [{configuration.DefaultMediaSource}]");
        Console.WriteLine($"  DefaultOrientation: {configuration.DefaultOrientation}");
        Console.WriteLine($"  DefaultResolutionDpi: {configuration.DefaultResolutionDpi}");
        return configuration;
    }

    // Returns whether the cancel call completed, for the check 7 summary.
    private static async Task<bool> CheckErrorPathsAsync(SpoolerPrintJobQueue jobs, PrinterId printerId)
    {
        Console.WriteLine();
        Console.WriteLine("Check 5 — a queue name that cannot exist.");
        var missingPrinter = new SpoolerPrinter(new SpoolerPrinterEndpoint(Guid.NewGuid().ToString("N")));
        try
        {
            _ = await missingPrinter.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine("  No exception was thrown. This is unexpected.");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Check 8 — cancelling an unknown job id on the real queue.");
        Console.WriteLine("  false is the expected result, not an exception.");
        try
        {
            var cancelled = await jobs.CancelJobAsync(printerId, "999999999", CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"  CancelJobAsync returned {cancelled}.");
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"  Threw {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    // Returns whether a job was actually submitted, for the check 7 summary. The options
    // are built from what this queue reported, so what a device mode carries and what it
    // cannot carry are both visible in one job.
    private static async Task<bool> CheckPrintCycleAsync(SpoolerPrinter printer, PrinterConfiguration configuration)
    {
        Console.WriteLine();
        Console.WriteLine("Check 3 — the print cycle.");
        if (!SampleHelpers.Confirm($"Send a tiny test payload to '{printer.Id}' twice, as two copies?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return false;
        }

        var options = DeviceModeOptions(configuration);
        Console.WriteLine($"  Asked for: Copies=2 Orientation={options.Orientation} MediaSize=[{options.MediaSize}] MediaSource=[{options.MediaSource}]");
        Console.WriteLine($"             MediaType=[{options.MediaType}] OutputBin=[{options.OutputBin}] NumberUp={options.NumberUp} PageRanges=1-1");

        var payload = PrinterPayload.FromString("Windows spooler test payload\n", PrinterContentTypes.Text);
        var submitted = await printer.PrintAsync(payload, options, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"  Job {submitted.JobId} submitted. Detail: {submitted.Detail}");
        Console.WriteLine($"  DroppedOptions: {String.Join(", ", submitted.DroppedOptions)}");
        Console.WriteLine("  MediaType, OutputBin, PageRanges and NumberUp must be in that list, and nothing else.");
        Console.WriteLine("  The print queue window must show two jobs, and this job id must be the first.");
        Console.WriteLine("  Open the job properties and compare the orientation, the paper and the tray.");
        return true;
    }

    // A name the queue never reported has no device mode number, so the first reported
    // name is used. An empty list leaves the option unset, which drops nothing.
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
}
