#nullable enable
using System.Diagnostics;
using System.Linq;
using AdaptArch.Devices.Pdf;
using AdaptArch.Devices.Pdfium;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.CupsHostTests;

/// <summary>
/// Tests against the CUPS daemon of the machine they run on, as the operating system spooler
/// of macOS and Linux reaches it: over the local socket, under the stock policy.
/// </summary>
/// <remarks>
/// The integration tests run a CUPS container whose policy allows everything, and a macOS
/// runner has no Docker at all. What only the host daemon can say is here: that a job belongs
/// to the user who printed it and that the same user can cancel it, what a local Mac queue is
/// sent, and what CUPS reports for a job the device behind a forwarding queue refused.
/// <para>
/// <c>pipeline/cups-host-queues.sh up</c> creates the two queues and prints the variables that
/// name them. Without <c>DEVICES_CUPS_HELD_QUEUE</c> and <c>DEVICES_CUPS_FORWARDING_QUEUE</c>
/// every test skips, so a machine that has not opted in is unaffected.
/// </para>
/// <para>
/// <b>Nothing prints.</b> The held queue is stopped and points at a port nothing listens on,
/// and every test checks that it is stopped before it sends anything; each test cancels what
/// it sent. The forwarding queue ends at an ippeveprinter, which keeps what it receives.
/// </para>
/// </remarks>
public class CupsHostTests
{
    private static readonly string? HeldQueue = Environment.GetEnvironmentVariable("DEVICES_CUPS_HELD_QUEUE");
    private static readonly string? ForwardingQueue = Environment.GetEnvironmentVariable("DEVICES_CUPS_FORWARDING_QUEUE");

    private const string SkipReason =
        "Needs the queues pipeline/cups-host-queues.sh creates, named in DEVICES_CUPS_HELD_QUEUE and DEVICES_CUPS_FORWARDING_QUEUE.";

    private const string ZplLabel = "^XA^FO50,50^ADN,36,20^FDdotnet-devices^FS^XZ";

    public static bool QueuesAreProvisioned =>
        !OperatingSystem.IsWindows() && !String.IsNullOrWhiteSpace(HeldQueue) && !String.IsNullOrWhiteSpace(ForwardingQueue);

    public static bool QueuesAreProvisionedOnAMac => QueuesAreProvisioned && OperatingSystem.IsMacOS();

    private static string Held => HeldQueue!;

    private static string Forwarding => ForwardingQueue!;

    // The name lp sends and PrintOptions.RequestingUserName defaults to.
    private static string ProcessUser =>
        String.IsNullOrWhiteSpace(Environment.UserName) ? PrintOptions.DefaultRequestingUserName : Environment.UserName;

    [Fact(SkipUnless = nameof(QueuesAreProvisioned), Skip = SkipReason)]
    public async Task DiscoverAsync_FindsBothQueuesOverTheLocalSocket()
    {
        var printers = await new SpoolerPrinterDiscovery().DiscoverAsync(TestContext.Current.CancellationToken);

        var ids = printers.Select(static printer => printer.Id).ToList();
        Assert.Contains(PrinterId.ForSpooler(Held), ids);
        Assert.Contains(PrinterId.ForSpooler(Forwarding), ids);
    }

    [Fact(SkipUnless = nameof(QueuesAreProvisioned), Skip = SkipReason)]
    public async Task GetConfigurationAsync_TellsAForwardingQueueFromALocalOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var held = await new SpoolerPrinter(new SpoolerPrinterEndpoint(Held)).GetConfigurationAsync(cancellationToken);
        var forwarding = await new SpoolerPrinter(new SpoolerPrinterEndpoint(Forwarding)).GetConfigurationAsync(cancellationToken);

        Assert.False(held.ForwardsOverIpp);
        Assert.True(forwarding.ForwardsOverIpp);
    }

    [Fact(SkipUnless = nameof(QueuesAreProvisioned), Skip = SkipReason)]
    public async Task JobQueue_AJobBelongsToThisProcessUser_WhoCanCancelIt()
    {
        // The stock policy answers a Cancel-Job that names no user with HTTP 401, which the
        // container's allow-everything policy could never show. The owner is read as lpstat
        // prints it, because the cancel alone proves little on a Mac: the policy also lets an
        // administrator, which a runner is, cancel a job that belongs to "anonymous".
        var cancellationToken = TestContext.Current.CancellationToken;
        await RequireHeldQueueAsync(cancellationToken);
        SpoolerPrintJobQueue queue = new();
        var id = PrinterId.ForSpooler(Held);

        var job = await new SpoolerPrinter(new SpoolerPrinterEndpoint(Held)).PrintAsync(
            PrinterPayload.FromString(ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "cups-host-owner" },
            cancellationToken);
        try
        {
            Assert.Equal(ProcessUser, await OwnerAsync(job.JobId, cancellationToken));
        }
        finally
        {
            Assert.True(await queue.CancelJobAsync(id, job.JobId, cancellationToken));
        }

        var after = await queue.GetJobAsync(id, job.JobId, cancellationToken);
        Assert.Equal(PrintJobState.Canceled, after?.State);
    }

    [Fact(SkipUnless = nameof(QueuesAreProvisionedOnAMac), Skip = "Needs the host queues on macOS, whose local daemon is offered URF alone.")]
    public async Task PrintAsync_APdfForTheLocalDaemonOfAMacIsSentAsUrf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await RequireHeldQueueAsync(cancellationToken);
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(Held))
        {
            Formats = new PrintFormatPolicy(null, [PdfiumPrinting.PdfConverter]),
        };

        var job = await printer.PrintAsync(
            PrinterPayload.FromBytes(OnePagePdf(), PrinterContentTypes.Pdf),
            new PrintOptions { JobName = "cups-host-urf", ConverterName = PdfiumPrinting.PdfConverter.Name },
            cancellationToken);
        try
        {
            Assert.Equal(PrinterContentTypes.Urf, job.SubmittedContentType);
            Assert.Equal(PdfiumPrinting.PdfConverter.Name, job.ConverterUsed);
        }
        finally
        {
            _ = await new SpoolerPrintJobQueue().CancelJobAsync(job.PrinterId, job.JobId, cancellationToken);
        }
    }

    [Fact(SkipUnless = nameof(QueuesAreProvisioned), Skip = SkipReason)]
    public async Task GetJobAsync_AJobTheDeviceRefusedIsCompletedWithAMessageAndAWarning()
    {
        // ippeveprinter refuses application/octet-stream, which is what a forwarding queue
        // sends a printer language on as. CUPS then reports the job as one that printed, and
        // only the message of its log says otherwise.
        var cancellationToken = TestContext.Current.CancellationToken;
        RecordingLoggerFactory log = new();
        SpoolerPrintJobQueue queue = new() { LoggerFactory = log };
        var job = await new SpoolerPrinter(new SpoolerPrinterEndpoint(Forwarding)).PrintAsync(
            PrinterPayload.FromString(ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "cups-host-refused" },
            cancellationToken);

        PrintJobInfo? read = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (DateTimeOffset.UtcNow < deadline)
        {
            read = await queue.GetJobAsync(job.PrinterId, job.JobId, cancellationToken);
            if (read?.State is PrintJobState.Completed or PrintJobState.Failed or PrintJobState.Canceled)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        try
        {
            Assert.NotNull(read);
            Assert.Equal(PrintJobState.Completed, read.State);
            Assert.False(String.IsNullOrWhiteSpace(read.PrinterStateMessage), "CUPS reported no message for the refused job.");
            Assert.Contains(1022, log.EventIds);
        }
        finally
        {
            if (read?.State is not PrintJobState.Completed)
            {
                _ = await queue.CancelJobAsync(job.PrinterId, job.JobId, cancellationToken);
            }
        }
    }

    // Read before anything is sent. A queue that is not stopped would try to deliver what the
    // tests submit.
    private static async Task RequireHeldQueueAsync(CancellationToken cancellationToken)
    {
        var status = await new SpoolerPrinter(new SpoolerPrinterEndpoint(Held)).GetStatusAsync(cancellationToken);

        Assert.True(
            status.State == PrinterStatusState.Paused,
            $"The queue '{Held}' reports {status.State} and these tests only send to a stopped one. Run: sh ./pipeline/cups-host-queues.sh up");
    }

    // lpstat -o prints one line for each job: "<queue>-<id> <owner> <size> <date>".
    private static async Task<string?> OwnerAsync(string jobId, CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("lpstat", ["-o", Held]) { RedirectStandardOutput = true };
        using var lpstat = Process.Start(start) ?? throw new InvalidOperationException("lpstat did not start.");
        var output = await lpstat.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await lpstat.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var line = output.Split('\n').FirstOrDefault(entry => entry.StartsWith($"{Held}-{jobId} ", StringComparison.Ordinal));
        return line?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
    }

    private static byte[] OnePagePdf() =>
        MinimalPdf.From([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            MinimalPdf.Stream("BT /F1 24 Tf 72 700 Td (dotnet-devices host test) Tj ET"),
            MinimalPdf.Helvetica,
        ]);
}
