#nullable enable
using System.Linq;
using System.Text;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// The <c>cups://</c> channel: a queue of a named server, reached over the network from any
/// operating system. Everything here goes through the public API, because a CUPS endpoint
/// carries its host and port.
/// </summary>
[Collection(CupsFixture.CollectionName)]
public class CupsServerTests
{
    private readonly CupsFixture _cups;

    public CupsServerTests(CupsFixture cups) => _cups = cups;

    [Fact]
    public async Task Discovery_FindsEveryQueueOfTheServer()
    {
        using HttpClient client = new();
        CupsPrinterDiscovery discovery = new(_cups.Host, _cups.Port, client, new IppTransportOptions());

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        var names = printers.Select(printer => printer.Id.TryGetQueueName(out var name) ? name : null).ToList();
        Assert.Contains(CupsFixture.RawQueue, names);
        Assert.Contains(CupsFixture.HeldQueue, names);
        Assert.Contains(CupsFixture.SecondQueue, names);

        // A queue of a named server is a cups:// channel, never a spooler:// one: the
        // spooler of this machine knows nothing about it.
        Assert.All(printers, printer => Assert.Equal(PrinterScheme.Cups, printer.Id.Scheme));
        Assert.All(printers, printer => Assert.IsType<CupsPrinterEndpoint>(printer.Endpoint));
    }

    [Fact]
    public async Task PrintAsync_PassesAPrinterLanguageThroughUnchanged()
    {
        using HttpClient client = new();
        CupsPrinter printer = new(_cups.EndpointFor(CupsFixture.RawQueue), client);
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "cups-passthrough" },
            cancellationToken);

        Assert.NotEmpty(job.JobId);

        var spooled = await _cups.SpooledDocumentAsync(job.JobId, cancellationToken);
        Assert.Equal(TestDocuments.ZplLabel, Encoding.UTF8.GetString(spooled));
    }

    [Fact]
    public async Task GetStatusAsync_ReadsTheStateOfTheQueue()
    {
        using HttpClient client = new();
        CupsPrinter live = new(_cups.EndpointFor(CupsFixture.RawQueue), client);
        CupsPrinter stopped = new(_cups.EndpointFor(CupsFixture.HeldQueue), client);
        var cancellationToken = TestContext.Current.CancellationToken;

        var liveStatus = await live.GetStatusAsync(cancellationToken);
        var stoppedStatus = await stopped.GetStatusAsync(cancellationToken);

        Assert.Equal(PrinterStatusState.Idle, liveStatus.State);

        // The queue was disabled with cupsdisable, which is IPP printer-state "stopped".
        // Reporting it as idle would make a stuck job invisible.
        Assert.NotEqual(PrinterStatusState.Idle, stoppedStatus.State);
        Assert.Contains(stoppedStatus.State, new[] { PrinterStatusState.Paused, PrinterStatusState.Error });
    }

    [Fact]
    public async Task JobQueue_ListsAJobOfAStoppedQueueAndCancelsIt()
    {
        using HttpClient client = new();
        CupsPrinter printer = new(_cups.EndpointFor(CupsFixture.HeldQueue), client);
        CupsPrintJobQueue queue = new(_cups.Host, _cups.Port, client, new IppTransportOptions());
        var id = _cups.IdFor(CupsFixture.HeldQueue);
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "cups-cancel" },
            cancellationToken);

        var read = await queue.GetJobAsync(id, job.JobId, cancellationToken);
        Assert.NotNull(read);
        Assert.Equal(PrintJobState.Queued, read.State);

        var listed = await queue.GetJobsAsync(id, cancellationToken);
        Assert.Contains(listed, other => other.JobId == job.JobId);

        Assert.True(await queue.CancelJobAsync(id, job.JobId, cancellationToken));

        var after = await queue.GetJobAsync(id, job.JobId, cancellationToken);
        Assert.NotNull(after);
        Assert.Equal(PrintJobState.Canceled, after.State);
    }

    [Fact]
    public async Task PrinterManager_ReachesACupsServerNamedInTheOptions()
    {
        ServiceCollection services = new();
        _ = services.AddPrinters(configureManager: options =>
        {
            options.IncludeMdns = false;
            // A cups:// server rides on the spooler discovery source, so turning that off
            // would silently drop the server named right below it.
            options.IncludeSpooler = true;
            options.Transports = [PrinterScheme.Cups];
            options.CupsServers.Add(new CupsServer(_cups.Host, _cups.Port));
        });

        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IPrinterManager>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var devices = await manager.DiscoverAsync(null, cancellationToken);
        var device = Assert.Single(devices, found => found.Id.TryGetQueueName(out var name) && name == CupsFixture.RawQueue);

        // The device key keeps the server and the queue. A key of the host alone would fuse
        // every queue behind one daemon into a single printer.
        Assert.Contains(CupsFixture.RawQueue, device.Key.ToString(), StringComparison.Ordinal);

        var job = await manager.PrintAsync(
            device.Id,
            PrinterPayload.FromString(TestDocuments.ZplLabel, PrinterContentTypes.Zpl),
            null,
            cancellationToken);

        Assert.NotEmpty(job.JobId);
    }
}
