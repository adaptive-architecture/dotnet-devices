#nullable enable
using System.Linq;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.InteropTests;

/// <summary>
/// The only tests in this repository that call <c>winspool.drv</c>.
/// </summary>
/// <remarks>
/// Everything else answers through <c>IWindowsSpoolerInterop</c>, and a fake cannot settle
/// the one question that matters most: a structure declared wrongly against the real header
/// still round-trips perfectly through a fake, because the fake writes it with the same
/// declaration it reads it with. Only bytes the spooler itself wrote say whether
/// <c>PRINTER_INFO_4</c>, <c>PRINTER_INFO_2</c> and <c>JOB_INFO_2</c> are right.
/// <para>
/// They run against a real queue, named in <c>DEVICES_TEST_QUEUE</c>, which
/// <c>pipeline/unit-test.sh</c> sets on Windows outside CI. Without that variable they
/// skip, so Linux, CI and a developer who has not opted in are all unaffected.
/// </para>
/// <para>
/// <b>The queue must be paused, and every test refuses to run against one that is not.</b>
/// That is not tidiness. A live queue prints paper, and Microsoft Print to PDF opens a
/// modal Save As dialog that no test can answer, which would hang the run on the machine
/// of whoever started it. Paused, nothing renders and every job stays in the queue where a
/// test can read it; each test cancels what it sent.
/// </para>
/// </remarks>
public class WindowsSpoolerInteropTests
{
    private static readonly string? Queue = Environment.GetEnvironmentVariable("DEVICES_TEST_QUEUE");

    public static bool QueueIsProvisioned => OperatingSystem.IsWindows() && !String.IsNullOrWhiteSpace(Queue);

    private const string SkipReason = "Needs the paused print queue named in DEVICES_TEST_QUEUE, which the Windows CI job creates.";

    private static string QueueName => Queue!;

    private static PrinterPayload Label(string text) =>
        PrinterPayload.FromString($"^XA^FD{text}^FS^XZ", PrinterContentTypes.Zpl);

    // Read once, before anything is sent. A queue that is not paused would print what the
    // tests below submit, and against Microsoft Print to PDF it would stop on a dialog.
    private static async Task RequirePausedQueueAsync(CancellationToken cancellationToken)
    {
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));
        var status = await printer.GetStatusAsync(cancellationToken).ConfigureAwait(false);

        Assert.True(
            status.State == PrinterStatusState.Paused,
            $"The queue '{QueueName}' reports {status.State} and these tests only run against a paused one. " +
            $"Pause it first: Get-CimInstance Win32_Printer -Filter \"Name='{QueueName}'\" | Invoke-CimMethod -MethodName Pause");
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task EnumeratePrinters_FindsTheQueueTheSpoolerReported()
    {
        // PRINTER_INFO_4: three fields, and the first structure this library ever reads.
        SpoolerPrinterDiscovery discovery = new();

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        var names = printers.Select(printer => printer.Id.Authority).ToList();
        Assert.Contains(QueueName, names);
        Assert.All(printers, printer => Assert.Equal(PrinterScheme.Spooler, printer.Id.Scheme));
        Assert.All(printers, printer => Assert.False(String.IsNullOrWhiteSpace(printer.Id.Authority)));
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task SubmitAndRead_ReadsBackEveryJobTheSpoolerWrote()
    {
        // This is the one a fake cannot do. EnumJobs returns JOB_INFO_2 structures
        // back to back, so a wrong field size does not show on the first job — it shows on
        // the ones after it, because every later field has moved. Three jobs, and the
        // second and the third are the evidence.
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));
        SpoolerPrintJobQueue queue = new();
        var id = PrinterId.ForSpooler(QueueName);
        var cancellationToken = TestContext.Current.CancellationToken;
        await RequirePausedQueueAsync(cancellationToken);

        var names = new[]
        {
            $"adaptarch-one-{Guid.NewGuid():N}",
            $"adaptarch-two-{Guid.NewGuid():N}",
            $"adaptarch-three-{Guid.NewGuid():N}",
        };

        List<string> submitted = [];
        try
        {
            foreach (var name in names)
            {
                var job = await printer.PrintAsync(Label(name), new PrintOptions { JobName = name }, cancellationToken);
                submitted.Add(job.JobId);
            }

            Assert.Equal(3, submitted.Distinct(StringComparer.Ordinal).Count());

            var jobs = await queue.GetJobsAsync(id, cancellationToken);
            foreach (var (jobId, name) in submitted.Zip(names))
            {
                var read = Assert.Single(jobs, job => job.JobId == jobId);

                // The document name sits behind five pointer fields in JOB_INFO_2. It is
                // what a wrong offset corrupts first, and it is read here for all three.
                Assert.Equal(name, read.JobName);
                Assert.Equal(id, read.PrinterId);
                Assert.True(read.ImpressionsCompleted is >= 0, $"'{name}' reported {read.ImpressionsCompleted} pages printed.");
            }
        }
        finally
        {
            foreach (var jobId in submitted)
            {
                _ = await queue.CancelJobAsync(id, jobId, cancellationToken);
            }
        }
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task GetJob_FindsOneJobAndNothingForAnIdThatIsNotThere()
    {
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));
        SpoolerPrintJobQueue queue = new();
        var id = PrinterId.ForSpooler(QueueName);
        var cancellationToken = TestContext.Current.CancellationToken;
        await RequirePausedQueueAsync(cancellationToken);

        var name = $"adaptarch-single-{Guid.NewGuid():N}";
        var job = await printer.PrintAsync(Label(name), new PrintOptions { JobName = name }, cancellationToken);
        try
        {
            var read = await queue.GetJobAsync(id, job.JobId, cancellationToken);
            Assert.NotNull(read);
            Assert.Equal(name, read.JobName);

            Assert.Null(await queue.GetJobAsync(id, "2147483646", cancellationToken));
        }
        finally
        {
            _ = await queue.CancelJobAsync(id, job.JobId, cancellationToken);
        }
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task CancelJob_RemovesTheJobAndAnswersFalseForOneThatIsGone()
    {
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));
        SpoolerPrintJobQueue queue = new();
        var id = PrinterId.ForSpooler(QueueName);
        var cancellationToken = TestContext.Current.CancellationToken;
        await RequirePausedQueueAsync(cancellationToken);

        var job = await printer.PrintAsync(Label("cancel"), new PrintOptions { JobName = $"adaptarch-cancel-{Guid.NewGuid():N}" }, cancellationToken);

        Assert.True(await queue.CancelJobAsync(id, job.JobId, cancellationToken));

        // SetJob answers ERROR_INVALID_PARAMETER for a job that left the queue, and the
        // driver turns that one code into false rather than into a failure. This is the
        // check that the real spooler really uses 87 for it.
        Assert.False(await queue.CancelJobAsync(id, "2147483646", cancellationToken));
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task GetStatus_ReadsPrinterInfo2OfAPausedQueue()
    {
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterId.ForSpooler(QueueName), status.PrinterId);

        // The queue is paused, so PRINTER_STATUS_PAUSED is set and the Detail text must name
        // it. An Idle answer here means the status word was read from the wrong offset of
        // PRINTER_INFO_2 — and it is also the guard every other test in this file leans on.
        Assert.Equal(PrinterStatusState.Paused, status.State);
        Assert.Contains("PAUSED", status.Detail ?? String.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task GetConfiguration_ReadsTheCapabilitiesOfARealDriver()
    {
        // DeviceCapabilities writes fixed-width name blocks and pairs of words, and
        // DocumentProperties answers with a DEVMODEW whose tail belongs to the driver. A
        // name cut short or full of stray characters is what a wrong block length looks
        // like, and no fake can produce a real driver's answer.
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(QueueName));

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterId.ForSpooler(QueueName), configuration.PrinterId);
        Assert.NotEmpty(configuration.MediaSizes);
        Assert.All(configuration.MediaSizes, size =>
        {
            Assert.False(String.IsNullOrWhiteSpace(size));
            Assert.DoesNotContain('\0', size);
        });
    }

    [Fact(SkipUnless = nameof(QueueIsProvisioned), Skip = SkipReason)]
    public async Task Operations_AQueueThatDoesNotExist_FailWithTheWindowsErrorText()
    {
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint($"adaptarch-missing-{Guid.NewGuid():N}"));
        var cancellationToken = TestContext.Current.CancellationToken;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => printer.GetStatusAsync(cancellationToken));

        // 1801 is ERROR_INVALID_PRINTER_NAME. The message carries what the operating system
        // said, which is the part no test on Linux can produce.
        Assert.Contains("1801", failure.Message, StringComparison.Ordinal);

        // DeviceCapabilities answers -1 rather than 0 for a queue that is not there, so the
        // configuration must fail rather than report a printer with no trays.
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => printer.GetConfigurationAsync(cancellationToken));
    }
}
