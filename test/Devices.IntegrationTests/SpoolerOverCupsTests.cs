#nullable enable
using System.Linq;
using System.Text;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// The <c>spooler://</c> channel on Linux and macOS, which is the local CUPS daemon reached
/// over IPP.
/// </summary>
/// <remarks>
/// <see cref="CupsSpoolerDriver"/> fixes the local daemon at <c>ipp://localhost:631/</c>,
/// and binding the container to port 631 of this machine would fight whatever cupsd the
/// developer already runs. The driver is therefore built on the internal constructor with
/// the container's address, and the endpoint, the printer, the queue and the discovery
/// above it are the shipped code unchanged.
/// </remarks>
[Collection(CupsFixture.CollectionName)]
public class SpoolerOverCupsTests
{
    private readonly CupsFixture _cups;

    public SpoolerOverCupsTests(CupsFixture cups) => _cups = cups;

    [Fact]
    public async Task Discovery_ReportsEachQueueAsASpoolerChannel()
    {
        using var client = new HttpClient();
        SpoolerPrinterDiscovery discovery = new(DriverFor(client));

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        var names = printers.Select(printer => printer.Id.Authority).ToList();
        Assert.Contains(CupsFixture.RawQueue, names);
        Assert.Contains(CupsFixture.HeldQueue, names);

        // A queue of the local daemon is a spooler:// channel and carries no host, which is
        // what keeps it apart from the same daemon reached as a cups:// server.
        Assert.All(printers, printer => Assert.Equal(PrinterScheme.Spooler, printer.Id.Scheme));
        Assert.All(printers, printer => Assert.Equal(DiscoverySource.Spooler, printer.Source));
        Assert.All(printers, printer => Assert.IsType<SpoolerPrinterEndpoint>(printer.Endpoint));
    }

    [Fact]
    public async Task PrintAsync_ReachesTheQueueAndKeepsTheBytes()
    {
        using var client = new HttpClient();
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.RawQueue), DriverFor(client));
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "spooler-passthrough" },
            cancellationToken);

        Assert.Equal(PrinterScheme.Spooler, job.PrinterId.Scheme);

        var spooled = await _cups.SpooledDocumentAsync(job.JobId, cancellationToken);
        Assert.Equal(TestDocuments.ZplLabel, Encoding.UTF8.GetString(spooled));
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsTheCapabilitiesOfTheQueue()
    {
        using var client = new HttpClient();
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.RawQueue), DriverFor(client));

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterId.ForSpooler(CupsFixture.RawQueue), configuration.PrinterId);
        Assert.Contains(PrinterContentTypes.Pdf, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);

        // A queue with no driver advertises no media, and the library must not invent any:
        // a caller that reads a paper size here would be reading a guess.
        Assert.Empty(configuration.MediaSizes);
    }

    [Fact]
    public async Task GetStatusAsync_SaysTheLocalQueueAnsweredOverIpp()
    {
        using var client = new HttpClient();
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.RawQueue), DriverFor(client));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Idle, status.State);

        // The CUPS spooler speaks IPP, so a spooler:// printer on Linux and macOS reports
        // the IPP transport that answered and not a spooler of its own.
        Assert.NotNull(status.Connection);
        Assert.Equal(PrinterScheme.Ipp, status.Connection.Scheme);
    }

    [Fact]
    public async Task JobQueue_WatchesAJobOfAStoppedQueueAndCancelsIt()
    {
        using var client = new HttpClient();
        var driver = DriverFor(client);
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.HeldQueue), driver);
        SpoolerPrintJobQueue queue = new(driver);
        var id = PrinterId.ForSpooler(CupsFixture.HeldQueue);
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "spooler-cancel" },
            cancellationToken);

        var jobs = await queue.GetJobsAsync(id, cancellationToken);
        Assert.Contains(jobs, listed => listed.JobId == job.JobId);

        Assert.True(await queue.CancelJobAsync(id, job.JobId, cancellationToken));

        var after = await queue.GetJobAsync(id, job.JobId, cancellationToken);
        Assert.NotNull(after);
        Assert.Equal(PrintJobState.Canceled, after.State);
    }

    [Fact]
    public async Task Monitor_ReportsTheJobUntilItIsCancelled()
    {
        using var client = new HttpClient();
        var driver = DriverFor(client);
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.HeldQueue), driver);
        SpoolerPrintJobQueue queue = new(driver);
        PollingPrintJobMonitor monitor = new(queue);
        var id = PrinterId.ForSpooler(CupsFixture.HeldQueue);
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "spooler-watch" },
            cancellationToken);

        PrintJobMonitorOptions watch = new()
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
            Timeout = TimeSpan.FromSeconds(20),
        };

        List<PrintJobState> seen = [];
        await foreach (var update in monitor.WatchJobAsync(id, job.JobId, watch, cancellationToken))
        {
            seen.Add(update.State);
            if (update.State == PrintJobState.Queued && seen.Count == 1)
            {
                // The queue is stopped, so nothing else will ever move this job. Cancelling
                // it is what gives the watch a terminal state to stop on.
                Assert.True(await queue.CancelJobAsync(id, job.JobId, cancellationToken));
            }
        }

        Assert.Equal(PrintJobState.Queued, seen[0]);
        Assert.Equal(PrintJobState.Canceled, seen[^1]);
    }

    private CupsSpoolerDriver DriverFor(HttpClient client) => new(client, _cups.BaseUri);
}
