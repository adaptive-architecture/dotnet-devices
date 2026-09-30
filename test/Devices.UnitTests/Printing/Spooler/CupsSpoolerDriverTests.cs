using System.Text;
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
        Assert.Equal(PrinterScheme.Spooler, printer.Id.Scheme);
        Assert.Equal("lobby", printer.Id.Authority);
        Assert.Equal(DiscoverySource.Spooler, printer.Source);
        var endpoint = Assert.IsType<SpoolerPrinterEndpoint>(printer.Endpoint);
        Assert.Equal("lobby", endpoint.Name);
    }

    [Fact]
    public async Task EnumeratePrintersAsync_ReportsTheQueueCupsMarksAsTheServerDefault()
    {
        var printers = await EnumerateTwoQueuesAsync(userDefault: null);

        Assert.False(printers.Single(printer => printer.Id.Authority == "lobby").Info.IsDefault);
        Assert.True(printers.Single(printer => printer.Id.Authority == "office").Info.IsDefault);
    }

    [Fact]
    public async Task EnumeratePrintersAsync_PrefersTheUserDefaultToTheServerDefault()
    {
        var printers = await EnumerateTwoQueuesAsync(userDefault: "lobby");

        Assert.True(printers.Single(printer => printer.Id.Authority == "lobby").Info.IsDefault);
        Assert.False(printers.Single(printer => printer.Id.Authority == "office").Info.IsDefault);
    }

    [Fact]
    public async Task EnumeratePrintersAsync_MarksNoQueueWhenTheUserDefaultNamesNone()
    {
        var printers = await EnumerateTwoQueuesAsync(userDefault: "gone");

        Assert.All(printers, printer => Assert.False(printer.Info.IsDefault));
    }

    // 0x23 is the enum tag CUPS answers printer-type with; 0x20000 is its default bit.
    private static Task<IReadOnlyList<DiscoveredPrinter>> EnumerateTwoQueuesAsync(string userDefault)
    {
        var body = IppMessages.Response(0x0000,
            (0x42, "printer-name", "lobby"),
            (0x23, "printer-type", 0x0004),
            (0x04, null, null),
            (0x42, "printer-name", "office"),
            (0x23, "printer-type", 0x20004));
        CupsSpoolerDriver driver = new(
            new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))),
            new Uri("ipp://localhost:631/"),
            userDefault: () => userDefault);
        return driver.EnumeratePrintersAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitAsync_SendsToTheQueueUriOnTheLocalDaemon()
    {
        // 0x02 is the job-attributes group: a job answer populates from no other tag.
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

    // CUPS receives the document as it is, so what only a renderer applies never reaches it.
    [Fact]
    public async Task SubmitAsync_ReportsWhatNoIppAttributeCarries()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(body));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions
            {
                Copies = 2,
                Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft },
                Smoothing = false,
                MediaSizeSource = MediaSizeSource.Document,
                FitArea = PrintFitArea.Physical,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [nameof(PrintOptions.FitArea), nameof(PrintOptions.MediaSizeSource), nameof(PrintOptions.Placement), nameof(PrintOptions.Smoothing)],
            job.DroppedOptions);
        Assert.All(job.DroppedOptionDetails, dropped =>
        {
            Assert.Equal(PrintOptionStage.Channel, dropped.Stage);
            Assert.Equal("CUPS receives the document as it is, and no IPP attribute carries it", dropped.Reason);
        });
    }

    [Fact]
    public async Task SubmitAsync_ReportsANamedConverterAsNotRun()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(body))));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { ConverterName = "pdfium" },
            TestContext.Current.CancellationToken);

        var dropped = Assert.Single(job.DroppedOptionDetails);
        Assert.Equal(nameof(PrintOptions.ConverterName), dropped.Option);
        Assert.Equal(PrintOptionStage.Conversion, dropped.Stage);
    }

    [Fact]
    public async Task SubmitAsync_ThrowsBeforeSendingWhenTheJobAsksToFailOnAnUnappliedOption()
    {
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(IppMessages.Response(0x0000)));
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { Smoothing = false, OnUnsupported = UnsupportedOptionBehavior.Throw },
            TestContext.Current.CancellationToken));

        Assert.Contains(nameof(PrintOptions.Smoothing), error.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SubmitAsync_SendsAPrinterLanguageAsTheCupsRawFormat()
    {
        // CUPS rejects application/vnd.zebra-zpl, and it re-types octet-stream as
        // text/plain for ZPL, which prints the command source on a page.
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        BodyCapturingHandler handler = new(body);
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        var text = Encoding.Latin1.GetString(handler.LastBody);
        Assert.Contains("application/vnd.cups-raw", text, StringComparison.Ordinal);
        Assert.DoesNotContain(PrinterContentTypes.Zpl, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_SendsAFormatCupsKnowsUnchanged()
    {
        var body = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        BodyCapturingHandler handler = new(body);
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        var text = Encoding.Latin1.GetString(handler.LastBody);
        Assert.Contains(PrinterContentTypes.Pdf, text, StringComparison.Ordinal);
        Assert.DoesNotContain("application/vnd.cups-raw", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_FitsADocumentOntoTheDefaultMediaOfTheQueue()
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("media-default", Latin1(handler.RequestBodies[0]), StringComparison.Ordinal);
        var job = handler.RequestBodies[1];
        Assert.True(HasKeyword(job, "media", "iso_a4_210x297mm"));
        Assert.True(HasKeyword(job, "print-scaling", "auto"));
        Assert.True(HasFitToPage(job));
    }

    [Fact]
    public async Task SubmitAsync_KeepsTheMediaAndTheFitTheJobNamed()
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { MediaSize = "na_letter_8.5x11in", Scaling = PrintScaling.Fit },
            TestContext.Current.CancellationToken);

        // The job named its media, so the queue is not asked for a default.
        var job = Assert.Single(handler.RequestBodies);
        Assert.True(HasKeyword(job, "media", "na_letter_8.5x11in"));
        Assert.False(HasKeyword(job, "media", "iso_a4_210x297mm"));
        Assert.True(HasKeyword(job, "print-scaling", "fit"));
        Assert.True(HasFitToPage(job));
    }

    [Fact]
    public async Task SubmitAsync_KeepsTheDimensionsTheJobNamed()
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { MediaDimensions = new MediaDimensions(PrintLength.FromMillimeters(100), PrintLength.FromMillimeters(150)) },
            TestContext.Current.CancellationToken);

        var job = Assert.Single(handler.RequestBodies);
        Assert.Contains("media-col", Latin1(job), StringComparison.Ordinal);
        Assert.DoesNotContain("iso_a4_210x297mm", Latin1(job), StringComparison.Ordinal);
        Assert.True(HasFitToPage(job));
    }

    [Fact]
    public async Task SubmitAsync_StillFitsWhenTheQueueNamesNoDefaultMedia()
    {
        IppMessages.CapturingHandler handler = new(SubmittedJob());
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        var job = handler.RequestBodies[1];
        Assert.False(HasKeyword(job, "media", "iso_a4_210x297mm"));
        Assert.True(HasKeyword(job, "print-scaling", "auto"));
        Assert.True(HasFitToPage(job));
    }

    // A page that is its own media keeps today's answer: with no media named, the queue
    // prints it at its own size.
    [Fact]
    public async Task SubmitAsync_LeavesAPageThatIsItsOwnMediaUnfitted()
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { MediaSizeSource = MediaSizeSource.Document },
            TestContext.Current.CancellationToken);

        var job = Assert.Single(handler.RequestBodies);
        Assert.False(HasFitToPage(job));
        Assert.DoesNotContain("iso_a4_210x297mm", Latin1(job), StringComparison.Ordinal);
    }

    // A placement never reaches CUPS, so the job lands where one without it would.
    [Fact]
    public async Task SubmitAsync_FitsADocumentWhosePlacementWasDropped()
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        var submitted = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft } },
            TestContext.Current.CancellationToken);

        Assert.Equal([nameof(PrintOptions.Placement)], submitted.DroppedOptions);
        Assert.True(HasFitToPage(handler.RequestBodies[1]));
    }

    [Theory]
    [InlineData("^XA^XZ", PrinterContentTypes.Zpl)]
    [InlineData("PNG", PrinterContentTypes.Png)]
    public async Task SubmitAsync_FitsNothingButADocument(string content, string contentType)
    {
        var handler = QueueWithDefaultMedia("iso_a4_210x297mm");
        CupsSpoolerDriver driver = new(new HttpClient(handler));

        _ = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString(content, contentType),
            null,
            TestContext.Current.CancellationToken);

        var job = Assert.Single(handler.RequestBodies);
        Assert.False(HasFitToPage(job));
        Assert.DoesNotContain("print-scaling", Latin1(job), StringComparison.Ordinal);
        Assert.DoesNotContain("iso_a4_210x297mm", Latin1(job), StringComparison.Ordinal);
    }

    // The fitted job goes out as an encoded request, so its failures must still read as the
    // typed call's do.
    [Fact]
    public async Task SubmitAsync_ReportsAnIppErrorOfAFittedJob()
    {
        // 0x040A is client-error-document-format-not-supported.
        var answers = new Queue<byte[]>([DefaultMedia("iso_a4_210x297mm"), IppMessages.Response(0x040A, 0x02)]);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(answers.Dequeue()))));

        var failure = await Assert.ThrowsAsync<PrinterOperationException>(() => driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken));

        Assert.Equal(0x040A, failure.IppStatusCode);
        Assert.Contains("does not accept the document format", failure.Message, StringComparison.Ordinal);
    }

    private static byte[] SubmittedJob() =>
        IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));

    // 0x44 is the keyword tag, which media-default is answered with.
    private static byte[] DefaultMedia(string media) =>
        IppMessages.Response(0x0000, (0x44, "media-default", media));

    private static QueueHandler QueueWithDefaultMedia(string media) => new(DefaultMedia(media), SubmittedJob());

    private static string Latin1(byte[] body) => Encoding.Latin1.GetString(body);

    // An attribute as RFC 8010 encodes it: the tag, the name and the value, each length first.
    private static bool HasKeyword(byte[] body, string name, string value) =>
        body.AsSpan().IndexOf(Encode(0x44, name, Encoding.ASCII.GetBytes(value))) >= 0;

    private static bool HasFitToPage(byte[] body) =>
        body.AsSpan().IndexOf(Encode(0x22, "fit-to-page", [1])) >= 0;

    private static byte[] Encode(byte tag, string name, byte[] value)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        List<byte> encoded = [tag, (byte)(nameBytes.Length >> 8), (byte)nameBytes.Length];
        encoded.AddRange(nameBytes);
        encoded.Add((byte)(value.Length >> 8));
        encoded.Add((byte)value.Length);
        encoded.AddRange(value);
        return [.. encoded];
    }

    // Answers Get-Printer-Attributes with the queue's attributes and every other operation
    // with the job, and keeps what each request sent.
    private sealed class QueueHandler : HttpMessageHandler
    {
        // The operation id is the second pair of octets of an RFC 8010 request.
        private const int GetPrinterAttributes = 0x000B;

        private readonly byte[] _attributes;
        private readonly byte[] _job;

        public QueueHandler(byte[] attributes, byte[] job)
        {
            _attributes = attributes;
            _job = job;
        }

        public List<byte[]> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            RequestBodies.Add(body);
            return IppMessages.Ok(((body[2] << 8) | body[3]) == GetPrinterAttributes ? _attributes : _job);
        }
    }

    private sealed class BodyCapturingHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBody;

        public BodyCapturingHandler(byte[] responseBody) => _responseBody = responseBody;

        public byte[] LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return IppMessages.Ok(_responseBody);
        }
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

        Assert.Equal(PrinterScheme.Spooler, status.PrinterId.Scheme);
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
        // 0x0406 is client-error-not-found. The daemon URI is fixed, so there is no probe.
        var notFound = IppMessages.Response(0x0406, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(notFound))));

        var job = await driver.GetJobAsync("lobby", "9999", TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    [Fact]
    public async Task GetJobAsync_ThrowsForAnIppErrorThatIsNotNotFound()
    {
        // 0x0501 is a real IPP error, so it must propagate, not read as "unknown job".
        var error = IppMessages.Response(0x0501, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(error))));

        _ = await Assert.ThrowsAsync<PrinterOperationException>(
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
        // 0x0501 is a real IPP error: it must throw, as in the GetJobAsync test.
        var error = IppMessages.Response(0x0501, 0x02);
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(error))));

        _ = await Assert.ThrowsAsync<PrinterOperationException>(
            () => driver.CancelJobAsync("lobby", "1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetJobAsync_ReportsTheMessageThatNamesTheCause()
    {
        // The channel of the reported failure: a job on a CUPS queue that stopped.
        var job = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 41),
            (0x23, "job-state", 6),
            (0x44, "job-state-reasons", "resources-are-not-ready"),
            (0x41, "job-printer-state-message", "Unable to connect: certificate expired."));
        CupsSpoolerDriver driver = new(new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(job))));

        var read = await driver.GetJobAsync("lobby", "41", TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Equal(["resources-are-not-ready"], read.StateReasons);
        Assert.Equal("Unable to connect: certificate expired.", read.PrinterStateMessage);
    }

    [Fact]
    public async Task GetJobAsync_TheRawSwitchOfTheTransportOptionsReachesTheQueue()
    {
        var job = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 41),
            (0x23, "job-state", 6),
            (0x44, "an-attribute-the-library-does-not-map", "a value"));
        CupsSpoolerDriver driver = new(
            new HttpClient(new IppMessages.StubHandler(_ => IppMessages.Ok(job))),
            new Uri("ipp://localhost:631/"),
            null,
            new IppTransportOptions { CaptureRawResponses = true });

        var read = await driver.GetJobAsync("lobby", "41", TestContext.Current.CancellationToken);

        Assert.NotNull(read);
        Assert.Contains(read.RawAttributes, attribute => attribute.Name == "an-attribute-the-library-does-not-map");
    }
}
