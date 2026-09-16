using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// What an application needs to answer "why does this job not print?".
public class IppDiagnosticsTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = NetworkPrinterEndpoint.Ipp("printer.local");
    private static readonly PrinterId Printer = PrinterId.ForIpp("printer.local");

    [Fact]
    public async Task GetStatusAsync_ReportsEachReasonAndTheReadableMessage()
    {
        var attributes = IppMessages.Response(
            0x0000,
            (0x23, "printer-state", 5),
            (0x44, "printer-state-reasons", "media-empty"),
            (0x44, "printer-state-reasons", "cups-pki-expired"),
            (0x41, "printer-state-message", "The printer certificate is expired."),
            (0x41, "printer-detailed-status-messages", "TLS handshake failed."));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["media-empty", "cups-pki-expired"], status.StateReasons);
        Assert.Equal("The printer certificate is expired.", status.StateMessage);
        Assert.Equal(["TLS handshake failed."], status.DetailedStatusMessages);

        // Detail keeps the shape it had before, so no caller breaks.
        Assert.Equal("media-empty; cups-pki-expired", status.Detail);
    }

    [Fact]
    public async Task GetStatusAsync_TheNoneKeywordIsNotAReason()
    {
        var attributes = IppMessages.Response(
            0x0000,
            (0x23, "printer-state", 3),
            (0x44, "printer-state-reasons", "none"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Empty(status.StateReasons);
        Assert.Null(status.Detail);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsTheTransportThatAnswered()
    {
        var attributes = IppMessages.Response(0x0000, (0x23, "printer-state", 3));

        // The IPPS endpoints do not answer, so the resolver goes down to plain IPP.
        IppMessages.StubHandler handler = new(request =>
            request.RequestUri.Scheme == "https"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions());

        Assert.Null(printer.Connection);
        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterScheme.Ipp, status.Connection?.Scheme);
        Assert.Equal("ipp://printer.local:631/ipp/print", status.Connection?.Endpoint.ToString());
        Assert.Equal(PrinterScheme.Ipp, printer.Connection?.Scheme);
    }

    [Fact]
    public async Task GetJobAsync_ReportsTheMessageThatNamesTheCause()
    {
        // A job that stopped: the state reason alone says nothing a user can act on, and the
        // spooler puts the cause in job-printer-state-message.
        var job = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 41),
            (0x23, "job-state", 6),
            (0x44, "job-state-reasons", "resources-are-not-ready"),
            (0x41, "job-state-message", "Job stopped."),
            (0x41, "job-printer-state-message", "Unable to connect to the printer: certificate expired."),
            (0x41, "job-detailed-status-messages", "cups-pki-expired"));
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(job))));

        var read = await queue.GetJobAsync(Printer, "41", TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(["resources-are-not-ready"], read.StateReasons);
        Assert.Equal("Job stopped.", read.StateMessage);
        Assert.Equal("Unable to connect to the printer: certificate expired.", read.PrinterStateMessage);
        Assert.Equal(["cups-pki-expired"], read.DetailedStatusMessages);
        Assert.Equal("resources-are-not-ready", read.Detail);
    }

    [Fact]
    public async Task GetJobsAsync_AsksForEveryAttributeTheMapperReads()
    {
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        IppMessages.CapturingHandler handler = new(job);
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(handler));

        _ = await queue.GetJobsAsync(Printer, TestContext.Current.CancellationToken);

        var text = System.Text.Encoding.Latin1.GetString(handler.RequestBodies[^1]);
        Assert.Contains("job-state-message", text, StringComparison.Ordinal);
        Assert.Contains("job-printer-state-message", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_ReportsAReasonTheSubmissionAnswerCarries()
    {
        // A printer may refuse a job in the answer to the submission itself.
        var job = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 9),
            (0x23, "job-state", 6),
            (0x44, "job-state-reasons", "job-hold-until-specified"),
            (0x41, "job-state-message", "The job is held."));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(job));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var submitted = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(["job-hold-until-specified"], submitted.StateReasons);
        Assert.Equal("The job is held.", submitted.StateMessage);
        Assert.Equal("job-hold-until-specified", submitted.Detail);
    }

    [Fact]
    public async Task RawAttributes_AreEmptyUntilTheSwitchIsOn()
    {
        var attributes = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions());

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Empty(status.RawAttributes);
    }

    [Fact]
    public async Task RawAttributes_CarryTheWholeAnswerWhenTheSwitchIsOn()
    {
        var attributes = IppMessages.Response(
            0x0000,
            (0x23, "printer-state", 3),
            (0x44, "an-attribute-the-library-does-not-map", "a value"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(
            Endpoint,
            new HttpClient(handler),
            null,
            new IppTransportOptions { CaptureRawResponses = true });

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Contains(
            status.RawAttributes,
            attribute => attribute.Name == "an-attribute-the-library-does-not-map" && attribute.Value == "a value");
        Assert.Contains(status.RawAttributes, attribute => attribute.Group == "printer");
    }

    [Fact]
    public async Task RawAttributes_ReachTheFailureWhenTheSwitchIsOn()
    {
        // 0x0400 is client-error-bad-request.
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, (0x23, "printer-state", 3)))
                : IppMessages.Ok(IppMessages.Response(0x0400, (0x44, "printer-state-reasons", "media-empty")));
        });
        using IppPrinter printer = new(
            Endpoint,
            new HttpClient(handler),
            null,
            new IppTransportOptions { CaptureRawResponses = true });

        var error = await Assert.ThrowsAsync<PrinterOperationException>(
            () => printer.GetStatusAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0x0400, error.IppStatusCode);
        Assert.Equal(Printer, error.PrinterId);
        Assert.NotEmpty(error.RawAttributes);
    }
}
