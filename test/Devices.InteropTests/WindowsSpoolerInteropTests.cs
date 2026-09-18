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
/// They run against a queue the CI job creates and pauses, named in
/// <c>DEVICES_TEST_QUEUE</c>. Without that variable they skip, so Linux and a developer's
/// machine are unaffected. **A failure to create the queue must fail the workflow step** —
/// a skip here would look exactly like a pass, which is the whole trap this is built to
/// avoid.
/// </para>
/// <para>
/// Nothing prints: the queue is paused, so every job stays in it where a test can read it.
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

        // The job creates the queue and pauses it, so PRINTER_STATUS_PAUSED is set and the
        // Detail text must name it. An Idle answer here means the status word was read from
        // the wrong offset of PRINTER_INFO_2.
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
