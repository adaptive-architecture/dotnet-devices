using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// The CUPS driver pointed at a named server rather than at the loopback: the calls are the
// same, and what changes is the address they go to and the channel each answer names.
public class CupsServerTests
{
    private static HttpClient Client(IppMessages.StubHandler handler) => new(handler);

    [Fact]
    public async Task Discovery_ReportsEachQueueAsACupsChannelOfTheServerItAsked()
    {
        var body = IppMessages.Response(0x0000,
            (0x42, "printer-name", "lobby"),
            (0x42, "printer-info", "Lobby LaserJet"),
            (0x42, "printer-location", "Reception"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsPrinterDiscovery discovery = new("printsrv", 8631, Client(handler), new IppTransportOptions());

        var printers = await discovery.DiscoverAsync(TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal("cups://printsrv:8631/lobby", printer.Id.ToString());
        Assert.Equal(DiscoverySource.CupsServer, printer.Source);
        var endpoint = Assert.IsType<CupsPrinterEndpoint>(printer.Endpoint);
        Assert.Equal("printsrv", endpoint.Host);
        Assert.Equal("lobby", endpoint.Name);
        Assert.Equal("Lobby LaserJet", printer.Info.Name);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("printsrv", request.RequestUri.Host);
        Assert.Equal(8631, request.RequestUri.Port);
    }

    [Fact]
    public async Task Printer_SubmitsToTheQueuePathOfTheNamedServer()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsPrinter printer = new(new CupsPrinterEndpoint("printsrv", "lobby"), Client(handler), new IppTransportOptions(), null);

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("7", job.JobId);

        // The job names the channel it was sent over, so it can be found again.
        Assert.Equal("cups://printsrv/lobby", job.PrinterId.ToString());
        Assert.Equal("cups://printsrv/lobby", printer.Id.ToString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("printsrv", request.RequestUri.Host);
        Assert.Equal("/printers/lobby", request.RequestUri.AbsolutePath);
    }

    // The identifier names the queue and not the security of the channel, so the transport
    // policy chooses. AllowPlainIpp is the same switch every other IPP channel reads.
    [Theory]
    [InlineData(true, "http")]
    [InlineData(false, "https")]
    public async Task Printer_TakesTheSchemeFromTheTransportPolicy(bool allowPlainIpp, string expected)
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsPrinter printer = new(
            new CupsPrinterEndpoint("printsrv", "lobby"),
            Client(handler),
            new IppTransportOptions { AllowPlainIpp = allowPlainIpp },
            null);

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, Assert.Single(handler.Requests).RequestUri.Scheme);
    }

    [Fact]
    public async Task JobQueue_ReadsTheQueueNamedByTheIdentifier()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 9));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsPrintJobQueue queue = new("printsrv", 631, Client(handler), new IppTransportOptions());

        var job = await queue.GetJobAsync(
            PrinterId.ForCups("printsrv", "lobby"),
            "7",
            TestContext.Current.CancellationToken);

        Assert.NotNull(job);
        Assert.Equal("cups://printsrv/lobby", job.PrinterId.ToString());
        Assert.Equal("/printers/lobby", Assert.Single(handler.Requests).RequestUri.AbsolutePath);
    }

    // One instance speaks to one server. Asking it about a queue of another would answer
    // about whatever queue of its own happens to share the name.
    [Fact]
    public async Task JobQueue_RefusesAQueueOfAnotherServer()
    {
        CupsPrintJobQueue queue = new("printsrv", 631, Client(new IppMessages.StubHandler(_ => IppMessages.Ok([]))), new IppTransportOptions());

        _ = await Assert.ThrowsAsync<NotSupportedException>(() =>
            queue.GetJobsAsync(PrinterId.ForCups("other", "lobby"), TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<NotSupportedException>(() =>
            queue.GetJobsAsync(PrinterId.ForSpooler("lobby"), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a host")]
    public void Discovery_RefusesAHostThatIsNotOne(string host) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new CupsPrinterDiscovery(host, 631, new HttpClient(), new IppTransportOptions()));

    [Fact]
    public void Endpoint_ComparesTheHostAndTheQueueWithoutRegardToCase()
    {
        CupsPrinterEndpoint left = new("PrintSrv", "Lobby");
        CupsPrinterEndpoint right = new("printsrv", "lobby");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new CupsPrinterEndpoint("printsrv", "lobby", 8631));
    }
}
