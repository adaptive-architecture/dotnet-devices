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
