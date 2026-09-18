#nullable enable
using System.Text;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// The printer in this collection reads PWG Raster and nothing else, which is the situation
/// the conversion path exists for. What it decides cannot be read off a unit test: the
/// decision is taken from the printer's own answer to Get-Printer-Attributes.
/// </summary>
[Collection(IppEveRasterFixture.CollectionName)]
public class IppConversionTests
{
    private readonly IppEveRasterFixture _printer;

    public IppConversionTests(IppEveRasterFixture printer) => _printer = printer;

    [Fact]
    public async Task GetConfigurationAsync_ReportsAPrinterThatCannotReadPdf()
    {
        using IppPrinter printer = new(_printer.Endpoint);

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(PrinterContentTypes.Pdf, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(PrinterContentTypes.PwgRaster, configuration.SupportedDocumentFormats, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrintAsync_ConvertsThePdfAndSendsWhatTheConverterProduced()
    {
        RecordingPayloadConverter converter = new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster);
        using IppPrinter printer = new(_printer.Endpoint)
        {
            Formats = new PrintFormatPolicy(null, [converter]),
        };

        var pdf = TestDocuments.OnePagePdf();
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);

        _ = await printer.PrintAsync(
            PrinterPayload.FromBytes(pdf, PrinterContentTypes.Pdf),
            new PrintOptions { JobName = "pdf-to-raster", ResolutionDpi = 300 },
            cancellationToken);

        Assert.Equal(1, converter.Calls);
        Assert.Equal(pdf, converter.LastInput);

        // The target is negotiated from what the printer answered, not from a constant.
        Assert.NotNull(converter.LastContext);
        Assert.Equal(PrinterContentTypes.PwgRaster, converter.LastContext.TargetContentType);
        Assert.Equal(PrinterContentTypes.Pdf, converter.LastContext.ContentType);

        var received = await _printer.ReceivedTextAsync(cancellationToken);
        Assert.StartsWith(RecordingPayloadConverter.Sentinel, received, StringComparison.Ordinal);
        Assert.DoesNotContain("%PDF", received, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_WithNoConverter_RefusesRatherThanPrintingNonsense()
    {
        using IppPrinter printer = new(_printer.Endpoint);

        // Nothing can render the document, so the job must fail. A printer that took the
        // bytes anyway would print the PDF source, or a blank page, and report success.
        _ = await Assert.ThrowsAnyAsync<Exception>(() => printer.PrintAsync(
            PrinterPayload.FromBytes(TestDocuments.OnePagePdf(), PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrintAsync_RealPwgRaster_ArrivesWithTheHeaderThePrinterExpects()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _printer.WaitUntilIdleAsync(cancellationToken);
        using IppPrinter printer = new(_printer.Endpoint);

        await using MemoryStream buffer = new();
        PwgRasterWriter writer = new(buffer, new PwgRasterOptions { ResolutionDpi = 300, TotalPageCount = 1 });
        var width = 64;
        var height = 8;
        writer.WritePage(new byte[writer.BytesPerLine(width) * height], width, height);
        var raster = buffer.ToArray();

        _ = await printer.PrintAsync(
            PrinterPayload.FromBytes(raster, PrinterContentTypes.PwgRaster),
            null,
            cancellationToken);

        var documents = await _printer.ReceivedDocumentsAsync(cancellationToken);
        var sent = Assert.Single(documents, document => document.Length == raster.Length);
        Assert.Equal(raster, sent);
        Assert.Equal("RaS2", Encoding.ASCII.GetString(sent, 0, 4));
    }
}
