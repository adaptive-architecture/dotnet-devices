using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.Samples;

// Makes the manual checks in docs/windows-manual-tests.md observable. None of the Windows
// spooler code has run on any machine in this repository, so every step here prints what it
// read next to what that reading became, not only the conclusion.
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
        var printerId = PrinterId.FromSpooler(queueName);

        await CheckJobListAsync(jobs, printerId).ConfigureAwait(false);
        await CheckPrinterStatusAsync(printer).ConfigureAwait(false);
        await CheckConfigurationAsync(printer).ConfigureAwait(false);
        var cancelledWithoutException = await CheckErrorPathsAsync(jobs, printerId).ConfigureAwait(false);

        var submitted = false;
        if (print)
        {
            submitted = await CheckPrintCycleAsync(printer).ConfigureAwait(false);
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

    // Check 7 asks whether PRINTER_ACCESS_USE is enough to read the status, reach the job
    // queue, cancel, and submit. The checks above already exercise most of that: if
    // CheckPrinterStatusAsync and CheckJobListAsync returned above without throwing, that
    // access level was enough for both; the two flags below report the two parts that
    // depend on what actually ran this time, so this never claims more than it saw.
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

    // A JOB_INFO_2 layout error leaves entry 0 correct and corrupts the entries after it,
    // because a wrong field size shifts every field that follows it. The reader must look
    // at the second and third job, not only the first.
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

    private static async Task CheckConfigurationAsync(SpoolerPrinter printer)
    {
        Console.WriteLine();
        Console.WriteLine("Check 6 — configuration.");
        var configuration = await printer.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
        foreach (var mediaSize in configuration.MediaSizes)
        {
            // The delimiters make truncation or a stray character visible, which a bare
            // print of the string would hide.
            Console.WriteLine($"  [{mediaSize}]");
        }

        Console.WriteLine($"  Resolutions (dpi): {String.Join(", ", configuration.SupportedResolutionsDpi)}");
        Console.WriteLine($"  SupportsDuplex: {configuration.SupportsDuplex}");
        Console.WriteLine($"  SupportsColor: {configuration.SupportsColor}");
    }

    // Returns whether the check 8 cancel call completed without throwing, so RunAsync can
    // report check 7's cancel-access coverage honestly instead of assuming it.
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

    // Returns whether a job was actually submitted, so RunAsync can report check 7's
    // submit-access coverage honestly: passing --print is not the same as the Confirm
    // gate below being accepted.
    private static async Task<bool> CheckPrintCycleAsync(SpoolerPrinter printer)
    {
        Console.WriteLine();
        Console.WriteLine("Check 3 — the print cycle.");
        if (!SampleHelpers.Confirm($"Send a tiny test payload to '{printer.Id.Value}'?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return false;
        }

        var payload = PrinterPayload.FromString("Windows spooler test payload\n", PrinterContentTypes.Text);
        var submitted = await printer.PrintAsync(payload, options: null, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"  Job {submitted.JobId} submitted.");
        Console.WriteLine("  Compare this job id against the Windows print queue window.");
        return true;
    }
}
