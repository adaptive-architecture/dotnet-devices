#nullable enable
using System.Linq;
using System.Text;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Pdfium;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.Rasterization;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// Rendering on a CUPS channel, against a real daemon and a real rasterizer: the target is
/// negotiated from what the queue answers, and the spooled file is what the converter wrote.
/// <see cref="CupsFixture.RawQueue"/> lists URF and PWG Raster, so URF is chosen;
/// <see cref="CupsFixture.PwgQueue"/> lists no URF, so the job falls back to PWG Raster.
/// </summary>
[Collection(CupsFixture.CollectionName)]
public class CupsRenderingTests
{
    private readonly CupsFixture _cups;

    public CupsRenderingTests(CupsFixture cups) => _cups = cups;

    [Fact]
    public async Task GetConfigurationAsync_TheRawQueueListsBothRasters()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, Pdfium());

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Contains(PrinterContentTypes.Urf, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(PrinterContentTypes.PwgRaster, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConfigurationAsync_ThePwgQueueListsPwgRasterButNotUrf()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.PwgQueue, client, Pdfium());

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Contains(PrinterContentTypes.PwgRaster, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(PrinterContentTypes.Urf, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrintAsync_ANamedConverter_SendsUrfToAQueueThatListsIt()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, Pdfium());
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PdfPayload(),
            new PrintOptions { JobName = "cups-pdfium-to-urf", ConverterName = PdfiumPrinting.PdfConverter.Name, ResolutionDpi = 150 },
            cancellationToken);

        Assert.Equal(PdfiumPrinting.PdfConverter.Name, job.ConverterUsed);
        Assert.Equal(PrinterContentTypes.Urf, job.SubmittedContentType);

        var (pageCount, pages) = UrfReader.Read(await _cups.SpooledDocumentAsync(job.JobId, cancellationToken));
        Assert.Equal(1, pageCount);
        Assert.Equal(150, Assert.Single(pages).ResolutionDpi);
    }

    [Fact]
    public async Task PrintAsync_ANamedConverter_SendsPwgRasterToAQueueWithoutUrf()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.PwgQueue, client, Pdfium());
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PdfPayload(),
            new PrintOptions { JobName = "cups-pdfium-to-pwg", ConverterName = PdfiumPrinting.PdfConverter.Name },
            cancellationToken);

        Assert.Equal(PdfiumPrinting.PdfConverter.Name, job.ConverterUsed);
        Assert.Equal(PrinterContentTypes.PwgRaster, job.SubmittedContentType);

        var spooled = await _cups.SpooledDocumentAsync(job.JobId, cancellationToken);
        Assert.Equal("RaS2", Encoding.ASCII.GetString(spooled, 0, 4));
        _ = Assert.Single(PwgRasterReader.Read(spooled));
    }

    [Fact]
    public async Task PrintAsync_ARequiredConverter_RendersAJobThatNamesNone()
    {
        using HttpClient client = new();
        PrintFormatPolicy formats = new(null, [PdfiumPrinting.PdfConverter], [new(PrinterContentTypes.Pdf, PdfiumPrinting.PdfConverter.Name)]);
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, formats);
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(PdfPayload(), new PrintOptions { JobName = "cups-required-converter" }, cancellationToken);

        Assert.Equal(PdfiumPrinting.PdfConverter.Name, job.ConverterUsed);
        Assert.Equal(PrinterContentTypes.Urf, job.SubmittedContentType);
        Assert.Equal("UNIRAST\0", Encoding.ASCII.GetString(await _cups.SpooledDocumentAsync(job.JobId, cancellationToken), 0, 8));
    }

    [Fact]
    public async Task PrintAsync_OverTheSpooler_RendersWithANamedConverter()
    {
        using HttpClient client = new();
        SpoolerPrinter printer = new(new SpoolerPrinterEndpoint(CupsFixture.RawQueue), new CupsSpoolerDriver(client, _cups.BaseUri, Pdfium()));
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PdfPayload(),
            new PrintOptions { JobName = "spooler-pdfium-to-urf", ConverterName = PdfiumPrinting.PdfConverter.Name },
            cancellationToken);

        Assert.Equal(PrinterScheme.Spooler, job.PrinterId.Scheme);
        Assert.Equal(PrinterContentTypes.Urf, job.SubmittedContentType);
        Assert.Equal("UNIRAST\0", Encoding.ASCII.GetString(await _cups.SpooledDocumentAsync(job.JobId, cancellationToken), 0, 8));
    }

    [Fact]
    public async Task PrintAsync_ARegisteredConverterNobodyNamed_LeavesThePdfToCupsAndReportsThePlacement()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, Pdfium());
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PdfPayload(),
            new PrintOptions { JobName = "cups-pdf-untouched", Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft } },
            cancellationToken);

        Assert.Null(job.ConverterUsed);
        Assert.Equal(PrinterContentTypes.Pdf, job.SubmittedContentType);
        var dropped = Assert.Single(job.DroppedOptionDetails);
        Assert.Equal(nameof(PrintOptions.Placement), dropped.Option);
        Assert.Equal(PrintOptionStage.Conversion, dropped.Stage);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(await _cups.SpooledDocumentAsync(job.JobId, cancellationToken), 0, 4));
    }

    [Fact]
    public async Task PrintAsync_APlacementWithANamedConverter_IsRenderedNotDropped()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, Pdfium());
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await printer.PrintAsync(
            PdfPayload(),
            new PrintOptions
            {
                JobName = "cups-placed-urf",
                ConverterName = PdfiumPrinting.PdfConverter.Name,
                Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft },
            },
            cancellationToken);

        Assert.Equal(PrinterContentTypes.Urf, job.SubmittedContentType);
        Assert.DoesNotContain(nameof(PrintOptions.Placement), job.DroppedOptions);
    }

    [Fact]
    public async Task PrintAsync_ANamedConverterNobodyRegistered_FailsBeforeSending()
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(CupsFixture.RawQueue, client, new PrintFormatPolicy(null, null));

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PdfPayload(),
            new PrintOptions { ConverterName = PdfiumPrinting.PdfConverter.Name },
            TestContext.Current.CancellationToken));
    }

    private static PrintFormatPolicy Pdfium() => new(null, [PdfiumPrinting.PdfConverter]);

    private static PrinterPayload PdfPayload() => PrinterPayload.FromBytes(TestDocuments.OnePagePdf(), PrinterContentTypes.Pdf);

    private CupsPrinter CupsPrinterFor(string queue, HttpClient client, PrintFormatPolicy formats) =>
        new(_cups.EndpointFor(queue), client, new IppTransportOptions(), formats);

    [Theory]
    [InlineData(CupsFixture.RawQueue)]
    [InlineData(CupsFixture.PwgQueue)]
    public async Task GetConfigurationAsync_AQueueThatWritesToAFileDoesNotForwardOverIpp(string queue)
    {
        using HttpClient client = new();
        var printer = CupsPrinterFor(queue, client, Pdfium());

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.False(configuration.ForwardsOverIpp);
    }
}
