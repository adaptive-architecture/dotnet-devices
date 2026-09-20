#nullable enable
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AdaptArch.Devices.Pdfium;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The converter rendering real PDFs with the real engine.
/// </summary>
/// <remarks>
/// PDFium is a native library that this package restores for Linux, macOS and Windows
/// alike, so unlike the in-box Windows engine it runs wherever the tests run. Nothing here
/// is faked, and the bytes are read back as the channel that receives them would read them.
/// </remarks>
public class PdfiumPdfConverterTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly IPrintPayloadConverter Converter = PdfiumPrinting.PdfConverter;

    [Fact]
    public async Task ConvertAsync_ToPng_AnswersWithOneImageAPage()
    {
        // The Windows spooler draws one GDI document a page, so the contract on this path
        // is one image a page and not one document.
        var pages = await Converter.ConvertAsync(
            TestPdf.WithPages(3), Context(PrinterContentTypes.Png, 300), TestContext.Current.CancellationToken);

        Assert.Equal(3, pages.Count);
        foreach (var page in pages)
        {
            Assert.Equal(PngSignature, page.Take(PngSignature.Length));

            // US Letter is 612x792 points, which at 300 dots an inch is 2550x3300.
            (var width, var height) = PngSize(page);
            Assert.Equal(2550, width);
            Assert.Equal(3300, height);
        }
    }

    [Fact]
    public async Task ConvertAsync_ToPwgRaster_AnswersWithOneDocumentForEveryPage()
    {
        // One PWG Raster stream carries every page, so a three-page document stays one
        // document. An answer of three would be sent as three jobs.
        var documents = await Converter.ConvertAsync(
            TestPdf.WithPages(3), Context(PrinterContentTypes.PwgRaster, 300), TestContext.Current.CancellationToken);

        var raster = Assert.Single(documents);
        Assert.Equal("RaS2", Encoding.ASCII.GetString(raster, 0, 4));
        Assert.Equal(3u, TotalPageCount(raster));
    }

    [Theory]
    [InlineData(null, 24)]
    [InlineData("srgb_8", 24)]
    [InlineData("sgray_8", 8)]
    public async Task ConvertAsync_ToPwgRaster_WritesTheColourSpaceThePrinterAskedFor(string? rasterType, uint bitsPerPixel)
    {
        var context = Context(PrinterContentTypes.PwgRaster, 300) with { RasterType = rasterType };

        var documents = await Converter.ConvertAsync(TestPdf.WithPages(1), context, TestContext.Current.CancellationToken);

        // Grayscale is one octet a pixel and comes straight out of PDFium; colour is three,
        // after the blue and red the engine writes the other way round are swapped.
        Assert.Equal(bitsPerPixel, BitsPerPixel(documents[0]));
    }

    [Fact]
    public async Task ConvertAsync_ColourPages_ComeBackInRedGreenBlueOrder()
    {
        // PDFium writes blue, green, red; both encoders read red, green, blue. A page of
        // one saturated colour is what tells a missing swap apart from a correct one: red
        // left unswapped arrives as blue, and no size or checksum notices.
        var pages = await Converter.ConvertAsync(
            TestPdf.RedSquare(), Context(PrinterContentTypes.Png, 150), TestContext.Current.CancellationToken);

        var pixels = PngPixels(Assert.Single(pages));

        Assert.Equal<byte[]>([255, 0, 0], pixels.Take(3).ToArray());
    }

    [Fact]
    public async Task ConvertAsync_RendersOnlyThePagesTheRangesName()
    {
        var context = new PrintConversionContext(
            PrinterContentTypes.Pdf, PrinterContentTypes.Png, 150, [new PageRange(2, 3)], "queue");

        var pages = await Converter.ConvertAsync(TestPdf.WithPages(5), context, TestContext.Current.CancellationToken);

        Assert.Equal(2, pages.Count);
    }

    [Fact]
    public async Task ConvertAsync_ClampsAResolutionTheEngineDoesNotRenderWell()
    {
        // 50 dots an inch turns a page to mush, so the floor of the band is used instead
        // and the pages come back at 150.
        var pages = await Converter.ConvertAsync(
            TestPdf.WithPages(1), Context(PrinterContentTypes.Png, 50), TestContext.Current.CancellationToken);

        (var width, var height) = PngSize(pages[0]);
        Assert.Equal(1275, width);
        Assert.Equal(1650, height);
    }

    [Fact]
    public async Task ConvertAsync_ARasterDocument_StatesTheClampedResolutionInItsHeader()
    {
        // The header must agree with the pixels, or the printer scales the page: the
        // resolution written is the one rendered at and not the one asked for.
        var documents = await Converter.ConvertAsync(
            TestPdf.WithPages(1), Context(PrinterContentTypes.PwgRaster, 1200), TestContext.Current.CancellationToken);

        Assert.Equal(600u, ResolutionDpi(documents[0]));
    }

    [Fact]
    public async Task ConvertAsync_ABrokenFile_SaysSoInsteadOfCrashing()
    {
        // PDFium reports a corrupt or password-protected file through FPDF_GetLastError,
        // which names nothing the caller can act on, so the message says what to look at.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Converter.ConvertAsync(
                Encoding.ASCII.GetBytes("%PDF-1.4\nnot a document at all\n"),
                Context(PrinterContentTypes.Png, 300),
                TestContext.Current.CancellationToken));

        Assert.Contains("corrupt or password-protected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_RangesThatSelectNoPage_FailRatherThanPrintNothing()
    {
        var context = new PrintConversionContext(
            PrinterContentTypes.Pdf, PrinterContentTypes.Png, 300, [new PageRange(7, 9)], "queue");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Converter.ConvertAsync(TestPdf.WithPages(2), context, TestContext.Current.CancellationToken));

        Assert.Contains("select no page", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_RejectsAMissingContext() =>
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Converter.ConvertAsync([1, 2, 3], null!, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ConvertAsync_RejectsMissingData() =>
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => Converter.ConvertAsync(null!, Context(PrinterContentTypes.Png, 300), TestContext.Current.CancellationToken));

    [Fact]
    public async Task ConvertAsync_ACancelledJob_StopsBeforeItRenders()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Converter.ConvertAsync(TestPdf.WithPages(2), Context(PrinterContentTypes.Png, 300), source.Token));
    }

    [Fact]
    public async Task ConvertAsync_ManyJobsAtOnce_AllRenderCorrectly()
    {
        // PDFium is not thread-safe and the manager may convert two jobs at the same time,
        // so the renderer serializes every call. Without that gate this test crashes the
        // process rather than failing, which is why it is here.
        var pdf = TestPdf.WithPages(2);
        var jobs = Enumerable.Range(0, 8).Select(index => Converter.ConvertAsync(
            pdf,
            Context(index % 2 == 0 ? PrinterContentTypes.Png : PrinterContentTypes.PwgRaster, 150),
            TestContext.Current.CancellationToken));

        var results = await Task.WhenAll(jobs);

        for (var index = 0; index < results.Length; index++)
        {
            Assert.Equal(index % 2 == 0 ? 2 : 1, results[index].Count);
        }
    }

    private static PrintConversionContext Context(string target, int dpi) =>
        new(PrinterContentTypes.Pdf, target, dpi, null, "queue");

    // RFC 2083 section 9.2: inflate the one IDAT chunk and drop the filter octet that
    // precedes every scanline. Only the first line is needed, and only filter 0 is written.
    private static byte[] PngPixels(byte[] png)
    {
        (var width, _) = PngSize(png);
        var stride = width * 3;

        var offset = PngSignature.Length;
        while (!String.Equals(Encoding.ASCII.GetString(png, offset + 4, 4), "IDAT", StringComparison.Ordinal))
        {
            offset += 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
        }

        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
        using MemoryStream compressed = new(png, offset + 8, length);
        using ZLibStream inflate = new(compressed, CompressionMode.Decompress);
        var scanline = new byte[stride + 1];
        inflate.ReadExactly(scanline);

        Assert.Equal(0, scanline[0]);
        return scanline[1..];
    }

    // RFC 2083 section 11.2.2: IHDR is the first chunk, and its data begins the dimensions.
    private static (int Width, int Height) PngSize(byte[] png) =>
        ((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)),
         (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)));

    // PWG 5102.4 Table 1, offsets inside the page header that follows the four-octet
    // synchronization word.
    private static uint ResolutionDpi(byte[] raster) => HeaderField(raster, 276);

    private static uint BitsPerPixel(byte[] raster) => HeaderField(raster, 388);

    private static uint TotalPageCount(byte[] raster) => HeaderField(raster, 452);

    private static uint HeaderField(byte[] raster, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(raster.AsSpan(4 + offset));
}
