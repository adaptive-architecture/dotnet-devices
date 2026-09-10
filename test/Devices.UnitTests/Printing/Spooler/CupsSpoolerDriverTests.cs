using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class CupsSpoolerDriverTests
{
    [Fact]
    public async Task EnumeratePrintersAsync_ReportsEachQueueAsASpoolerEndpoint()
    {
        var body = IppMessages.Response(0x0000,
            (0x42, "printer-name", "lobby"),
            (0x42, "printer-info", "Lobby LaserJet"),
            (0x42, "printer-location", "Reception"));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var printers = await driver.EnumeratePrintersAsync(TestContext.Current.CancellationToken);

        var printer = Assert.Single(printers);
        Assert.Equal(PrinterIdKind.Spooler, printer.Id.Kind);
        Assert.Equal("lobby", printer.Id.Value);
        Assert.Equal(DiscoverySource.Spooler, printer.Source);
        var endpoint = Assert.IsType<SpoolerPrinterEndpoint>(printer.Endpoint);
        Assert.Equal("lobby", endpoint.Name);
    }

    [Fact]
    public async Task SubmitAsync_SendsToTheQueueUriOnTheLocalDaemon()
    {
        // 0x02 is the job-attributes group. A job-shaped answer only populates from this
        // group tag, not the printer-attributes-tag (0x04) the default overload uses.
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("7", job.JobId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("localhost", request.RequestUri.Host);
        Assert.Equal(631, request.RequestUri.Port);
        Assert.Equal("/printers/lobby", request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task SubmitAsync_EscapesTheQueueNameInTheRequestUri()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "Lobby Printer",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/printers/Lobby%20Printer", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsTheStateOfTheQueue()
    {
        var body = IppMessages.Response(0x0000,
            (0x23, "printer-state", 4),
            (0x44, "printer-state-reasons", "media-empty"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var status = await driver.GetStatusAsync("lobby", TestContext.Current.CancellationToken);

        Assert.Equal(PrinterIdKind.Spooler, status.PrinterId.Kind);
        Assert.Equal(PrinterStatusState.Processing, status.State);
        Assert.Equal("media-empty", status.Detail);
        Assert.Equal("/printers/lobby", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReportsTheCapabilitiesOfTheQueue()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "two-sided-long-edge"),
            (0x22, "color-supported", (byte)1),
            (0x44, "media-supported", "iso_a4_210x297mm"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var configuration = await driver.GetConfigurationAsync("lobby", TestContext.Current.CancellationToken);

        Assert.True(configuration.SupportsDuplex);
        Assert.True(configuration.SupportsColor);
        Assert.Equal(["iso_a4_210x297mm"], configuration.MediaSizes);
        Assert.Equal("/printers/lobby", Assert.Single(handler.Requests).RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetJobsAsync_MapsEachJob()
    {
        // 0x02 is the job-attributes group; see the comment in SubmitAsync's test above.
        var body = IppMessages.Response(0x0000, 0x02,
            (0x21, "job-id", 7),
            (0x23, "job-state", 5),
            (0x21, "job-impressions", 4),
            (0x21, "job-impressions-completed", 1));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var jobs = await driver.GetJobsAsync("lobby", TestContext.Current.CancellationToken);

        var job = Assert.Single(jobs);
        Assert.Equal(PrintJobState.Printing, job.State);
        Assert.Equal(1, job.ImpressionsCompleted);
        Assert.Equal(4, job.TotalImpressions);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenTheIdentifierIsNotANumberAndSendsNoRequest()
    {
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(IppMessages.Response(0x0000, 0x02)));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var job = await driver.GetJobAsync("lobby", "not-a-number", TestContext.Current.CancellationToken);

        Assert.Null(job);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetJobAsync_ReturnsNullWhenThePrinterDoesNotKnowTheJob()
    {
        // 0x0406 is client-error-not-found. Unlike IppPrintJobQueue, there is no resolver
        // probe here: the daemon URI is fixed, so this is the only request the call sends.
        var notFound = IppMessages.Response(0x0406, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(notFound))));

        var job = await driver.GetJobAsync("lobby", "9999", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task GetJobAsync_ThrowsForAnIppErrorThatIsNotNotFound()
    {
        // 0x0501 is server-error-operation-not-supported: a real IPP error, but not
        // client-error-not-found, so it must propagate rather than read as "unknown job".
        var error = IppMessages.Response(0x0501, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(error))));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.GetJobAsync("lobby", "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelJobAsync_ReturnsFalseWhenThePrinterDoesNotKnowTheJob()
    {
        // 0x0406 is client-error-not-found.
        var notFound = IppMessages.Response(0x0406, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(notFound))));

        var canceled = await driver.CancelJobAsync("lobby", "1", TestContext.Current.CancellationToken);

        Assert.False(canceled);
    }

    [Fact]
    public async Task CancelJobAsync_ThrowsForAnIppErrorThatIsNotNotFound()
    {
        // 0x0501 is server-error-operation-not-supported: see the equivalent GetJobAsync
        // test for why this must throw rather than report false.
        var error = IppMessages.Response(0x0501, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(error))));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => driver.CancelJobAsync("lobby", "1", TestContext.Current.CancellationToken));
    }
}
