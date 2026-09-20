using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Proves what reaches the wire. CUPS rejects a printer language outright, and it re-types
// application/octet-stream by reading the bytes, so the document-format the printer
// receives decides whether a label prints or the ZPL source prints as text.
public class IppPrinterDocumentFormatTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = NetworkPrinterEndpoint.Ipp("printer.local");

    // 0x02 is the job-attributes group: a job answer populates from no other tag.
    private static byte[] JobResponse() =>
        IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 5), (0x23, "job-state", 3));

    private static byte[] AttributesResponse(params string[] formats)
    {
        // 0x49 is the mimeMediaType tag. Only the first value carries the name.
        var attributes = new (byte, string, object)[formats.Length];
        for (var i = 0; i < formats.Length; i++)
        {
            attributes[i] = (0x49, i == 0 ? "document-format-supported" : null, formats[i]);
        }

        return IppMessages.Response(0x0000, attributes);
    }

    private static async Task<string> SentFormatForAsync(string contentType, params string[] supported)
    {
        OperationHandler handler = new(AttributesResponse(supported), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", contentType),
            null,
            TestContext.Current.CancellationToken);

        return Encoding.Latin1.GetString(handler.PrintJobBody);
    }

    [Fact]
    public async Task PrintAsync_SendsTheCupsRawFormatToACupsServer()
    {
        var body = await SentFormatForAsync(PrinterContentTypes.Zpl, PrinterContentTypes.Pdf, IppDocumentFormat.CupsRaw);

        Assert.Contains(IppDocumentFormat.CupsRaw, body, StringComparison.Ordinal);
        Assert.DoesNotContain(PrinterContentTypes.Zpl, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_SendsOctetStreamToAPrinterThatIsNotCups()
    {
        var body = await SentFormatForAsync(PrinterContentTypes.Zpl, PrinterContentTypes.Pdf, PrinterContentTypes.OctetStream);

        Assert.Contains(PrinterContentTypes.OctetStream, body, StringComparison.Ordinal);
        Assert.DoesNotContain(IppDocumentFormat.CupsRaw, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_KeepsTheLanguageWhenThePrinterNamesItItself()
    {
        var body = await SentFormatForAsync(PrinterContentTypes.Zpl, PrinterContentTypes.Zpl, IppDocumentFormat.CupsRaw);

        Assert.Contains(PrinterContentTypes.Zpl, body, StringComparison.Ordinal);
        Assert.DoesNotContain(IppDocumentFormat.CupsRaw, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_FallsBackToOctetStreamWhenThePrinterReportsNoFormats()
    {
        var body = await SentFormatForAsync(PrinterContentTypes.Zpl);

        Assert.Contains(PrinterContentTypes.OctetStream, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_AsksForNoConfigurationWhenTheFormatNeedsNoNegotiation()
    {
        OperationHandler handler = new(AttributesResponse(PrinterContentTypes.Pdf), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        // One probe by the resolver, and the print. A PDF needs no format list.
        Assert.Equal(1, handler.AttributeRequests);
        Assert.Contains(PrinterContentTypes.Pdf, Encoding.Latin1.GetString(handler.PrintJobBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_ReadsTheFormatListOneTimeForRepeatedJobs()
    {
        OperationHandler handler = new(AttributesResponse(IppDocumentFormat.CupsRaw), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));
        var payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        _ = await printer.PrintAsync(payload, null, TestContext.Current.CancellationToken);
        _ = await printer.PrintAsync(payload, null, TestContext.Current.CancellationToken);

        // The resolver probe, and one configuration read that both jobs share.
        Assert.Equal(2, handler.AttributeRequests);
    }

    [Fact]
    public async Task PrintAsync_NamesTheFormatWhenThePrinterRejectsIt()
    {
        // 0x040A is client-error-document-format-not-supported.
        OperationHandler handler = new(
            AttributesResponse(PrinterContentTypes.Pdf),
            IppMessages.Response(0x040A, 0x02));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<PrinterOperationException>(() => printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));

        Assert.Contains(PrinterContentTypes.OctetStream, exception.Message, StringComparison.Ordinal);
        Assert.Contains("document format", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrintAsync_ConvertsADocumentThePrinterCannotRead()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        var body = await PrintPdfAsync(converter, null, PrinterContentTypes.PwgRaster);

        Assert.Equal(1, converter.Calls);
        Assert.Contains(PrinterContentTypes.PwgRaster, body, StringComparison.Ordinal);
        Assert.Contains(FakeConverter.Marker, body, StringComparison.Ordinal);
        Assert.DoesNotContain("%PDF", body, StringComparison.Ordinal);
    }

    // The printer reads PDF itself, so the document goes as it is. A raster of a document
    // is never better than the document.
    [Fact]
    public async Task PrintAsync_KeepsTheDocumentWhenThePrinterReadsIt()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        var body = await PrintPdfAsync(converter, null, PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster);

        Assert.Equal(0, converter.Calls);
        Assert.Contains(PrinterContentTypes.Pdf, body, StringComparison.Ordinal);
        Assert.Contains("%PDF", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_DoesNotConvertWhenThePrinterReadsNothingTheConverterWrites()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        var body = await PrintPdfAsync(converter, null, PrinterContentTypes.Jpeg);

        Assert.Equal(0, converter.Calls);
        Assert.Contains(PrinterContentTypes.Pdf, body, StringComparison.Ordinal);
    }

    // An application that registered no converter must not pay for the format list it
    // would never have used: one probe by the resolver, and no configuration read.
    [Fact]
    public async Task PrintAsync_ReadsNoFormatListWhenNoConverterIsRegistered()
    {
        OperationHandler handler = new(AttributesResponse(PrinterContentTypes.PwgRaster), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.AttributeRequests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task PrintAsync_FailsWhenTheConverterDoesNotReturnOneDocument(int documents)
    {
        FakeConverter converter = new(documents, PrinterContentTypes.PwgRaster);
        OperationHandler handler = new(AttributesResponse(PrinterContentTypes.PwgRaster), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler)) { Formats = new PrintFormatPolicy(null, [converter]) };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken));

        Assert.Contains(PrinterContentTypes.PwgRaster, exception.Message, StringComparison.Ordinal);

        // Nothing reached the printer: a converter that broke the contract spools nothing.
        Assert.Null(handler.PrintJobBody);
    }

    // The converter selected the pages, so repeating them on the job would select a
    // subset of the subset.
    [Fact]
    public async Task PrintAsync_GivesThePageRangesToTheConverterAndSendsNoneToThePrinter()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        PrintOptions options = new() { PageRanges = [new PageRange(2, 3)] };
        var body = await PrintPdfAsync(converter, options, PrinterContentTypes.PwgRaster);

        var range = Assert.Single(converter.LastContext.PageRanges);
        Assert.Equal(2, range.Lower);
        Assert.Equal(3, range.Upper);
        Assert.DoesNotContain("page-ranges", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_ConvertsAtTheDefaultResolutionWhenTheJobNamesNone()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        _ = await PrintPdfAsync(converter, null, PrinterContentTypes.PwgRaster);

        Assert.Equal(PrintConversionContext.DefaultDpi, converter.LastContext.Dpi);
    }

    [Fact]
    public async Task PrintAsync_ConvertsAtTheResolutionTheJobAskedFor()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        _ = await PrintPdfAsync(converter, new PrintOptions { ResolutionDpi = 600 }, PrinterContentTypes.PwgRaster);

        Assert.Equal(600, converter.LastContext.Dpi);
    }

    // A raster is only readable at a resolution the printer rasters at, so a request it
    // cannot meet moves to the nearest one it named instead of being refused.
    [Theory]
    [InlineData(null, 300)]
    [InlineData(300, 300)]
    [InlineData(400, 300)]
    [InlineData(500, 600)]
    [InlineData(2400, 600)]
    public async Task PrintAsync_ConvertsAtTheNearestResolutionThePrinterRasters(int? asked, int expected)
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        OperationHandler handler = new(RasterAttributes(), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler)) { Formats = new PrintFormatPolicy(null, [converter]) };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            asked is null ? null : new PrintOptions { ResolutionDpi = asked },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, converter.LastContext.Dpi);
    }

    [Theory]
    [InlineData(null, "srgb_8")]
    [InlineData(PrintColorMode.Color, "srgb_8")]
    [InlineData(PrintColorMode.Monochrome, "sgray_8")]
    public async Task PrintAsync_ChoosesTheRasterTypeFromTheColorMode(PrintColorMode? mode, string expected)
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        OperationHandler handler = new(RasterAttributes(), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler)) { Formats = new PrintFormatPolicy(null, [converter]) };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            mode is null ? null : new PrintOptions { ColorMode = mode },
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, converter.LastContext.RasterType);
    }

    [Fact]
    public async Task PrintAsync_PassesTheSheetBackAndTheDuplexModeToTheConverter()
    {
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);
        OperationHandler handler = new(RasterAttributes(), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler)) { Formats = new PrintFormatPolicy(null, [converter]) };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { Duplex = DuplexMode.LongEdge },
            TestContext.Current.CancellationToken);

        Assert.Equal("rotated", converter.LastContext.SheetBack);
        Assert.Equal(DuplexMode.LongEdge, converter.LastContext.Duplex);
    }

    // A printer that reads PWG Raster, rasters at 300 and 600, and turns its sheets over.
    private static byte[] RasterAttributes() =>
        IppMessages.Response(0x0000,
            (0x49, "document-format-supported", PrinterContentTypes.PwgRaster),
            (0x44, "pwg-raster-document-type-supported", "sgray_8"),
            (0x44, null, "srgb_8"),
            (0x32, "pwg-raster-document-resolution-supported", Resolution(300)),
            (0x32, null, Resolution(600)),
            (0x44, "pwg-raster-document-sheet-back", "rotated"));

    // RFC 8010: width and height as 4-byte integers, then the unit 3 for dots an inch.
    private static byte[] Resolution(int dpi) =>
    [
        (byte)(dpi >> 24), (byte)(dpi >> 16), (byte)(dpi >> 8), (byte)dpi,
        (byte)(dpi >> 24), (byte)(dpi >> 16), (byte)(dpi >> 8), (byte)dpi,
        3,
    ];

    [Fact]
    public async Task PrintAsync_APdfNamingAConverter_ConvertsWithThatOneAndNotThePreferredOne()
    {
        FakeConverter preferred = new(1, PrinterContentTypes.PwgRaster) { Name = "Preferred" };
        FakeConverter asked = new(1, PrinterContentTypes.PwgRaster) { Name = "Asked" };
        OperationHandler handler = new(AttributesResponse(PrinterContentTypes.PwgRaster), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler))
        {
            Formats = new PrintFormatPolicy(null, [preferred, asked]),
        };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { ConverterName = "asked" },
            TestContext.Current.CancellationToken);

        // Case-insensitive, and the one registered first does not run although it reads PDF.
        Assert.Equal(1, asked.Calls);
        Assert.Equal(0, preferred.Calls);
    }

    [Fact]
    public async Task PrintAsync_APdfNamingAConverterNobodyCarries_FailsInsteadOfSendingThePdf()
    {
        FakeConverter real = new(1, PrinterContentTypes.PwgRaster) { Name = "Real" };
        OperationHandler handler = new(AttributesResponse(PrinterContentTypes.PwgRaster), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler))
        {
            Formats = new PrintFormatPolicy(null, [real]),
        };

        // Without a name, a missing converter sends the document unchanged for the printer
        // to judge. With one, silence would send a PDF to a printer that cannot read it and
        // report success, so the job fails here and names what this process registered.
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            new PrintOptions { ConverterName = "Imaginary" },
            TestContext.Current.CancellationToken));

        Assert.Contains("'Imaginary'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Real'", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, real.Calls);
        Assert.Null(handler.PrintJobBody);
    }

    [Fact]
    public async Task PrintAsync_APdfThePrinterReads_IsSentUnchangedWhenNoConverterIsNamed()
    {
        // The default, and the reason the default is what it is: the printer's own
        // interpreter beats any raster of ours, and the job is a fraction of the size.
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster);

        var body = await PrintPdfAsync(converter, null, PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster);

        Assert.Equal(0, converter.Calls);
        Assert.Contains(PrinterContentTypes.Pdf, body, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeConverter.Marker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_APdfNamingAConverter_ConvertsEvenWhereThePrinterReadsPdf()
    {
        // Naming a converter is the one way of saying the document is not what should be
        // sent. Nobody sets that as a preference, so a printer that happens to read PDF as
        // well must not quietly decide the named engine should not run.
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster) { Name = "Named" };

        var body = await PrintPdfAsync(
            converter,
            new PrintOptions { ConverterName = "Named" },
            PrinterContentTypes.Pdf,
            PrinterContentTypes.PwgRaster);

        Assert.Equal(1, converter.Calls);
        Assert.Contains(FakeConverter.Marker, body, StringComparison.Ordinal);
        Assert.Contains(PrinterContentTypes.PwgRaster, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_ANamedConverterThePrinterCannotTake_StillSendsTheDocument()
    {
        // The printer reads the PDF and reads nothing this converter writes. Failing a job
        // that will print correctly would be the worse answer, so it passes through; the log
        // is what says the name went nowhere.
        FakeConverter converter = new(1, PrinterContentTypes.PwgRaster) { Name = "Named" };

        var body = await PrintPdfAsync(
            converter,
            new PrintOptions { ConverterName = "Named" },
            PrinterContentTypes.Pdf,
            PrinterContentTypes.Jpeg);

        Assert.Equal(0, converter.Calls);
        Assert.Contains(PrinterContentTypes.Pdf, body, StringComparison.Ordinal);
    }

    private static async Task<string> PrintPdfAsync(
        FakeConverter converter,
        PrintOptions options,
        params string[] supported)
    {
        OperationHandler handler = new(AttributesResponse(supported), JobResponse());
        using IppPrinter printer = new(Endpoint, new HttpClient(handler)) { Formats = new PrintFormatPolicy(null, [converter]) };

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            options,
            TestContext.Current.CancellationToken);

        return Encoding.Latin1.GetString(handler.PrintJobBody);
    }

    private sealed class FakeConverter : IPrintPayloadConverter
    {
        internal const string Marker = "CONVERTED";

        private readonly int _documents;
        private readonly string[] _targets;

        public FakeConverter(int documents, params string[] targets)
        {
            _documents = documents;
            _targets = targets;
        }

        // Set it where a test registers two of these and has to tell them apart.
        public string Name { get; init; } = nameof(FakeConverter);

        public int Calls { get; private set; }

        public PrintConversionContext LastContext { get; private set; }

        public bool CanConvert(string contentType) =>
            String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

        public bool CanEmit(string targetContentType) =>
            Array.Exists(_targets, target => String.Equals(target, targetContentType, StringComparison.OrdinalIgnoreCase));

        public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            LastContext = context;

            List<byte[]> documents = new(_documents);
            for (var i = 0; i < _documents; i++)
            {
                documents.Add(Encoding.Latin1.GetBytes(Marker));
            }

            return Task.FromResult<IReadOnlyList<byte[]>>(documents);
        }
    }

    // Answers by IPP operation, so a test does not depend on the order of the requests.
    private sealed class OperationHandler : HttpMessageHandler
    {
        private const int GetPrinterAttributes = 0x000B;
        private const int PrintJob = 0x0002;

        private readonly byte[] _attributes;
        private readonly byte[] _job;

        public OperationHandler(byte[] attributes, byte[] job)
        {
            _attributes = attributes;
            _job = job;
        }

        public int AttributeRequests { get; private set; }

        public byte[] PrintJobBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            // RFC 8010: version (2 bytes), then the operation identifier (2 bytes).
            var operation = (body[2] << 8) | body[3];
            if (operation == PrintJob)
            {
                PrintJobBody = body;
                return IppMessages.Ok(_job);
            }

            if (operation == GetPrinterAttributes)
            {
                AttributeRequests++;
                return IppMessages.Ok(_attributes);
            }

            throw new NotSupportedException($"The test handler received operation 0x{operation:X4}.");
        }
    }
}
