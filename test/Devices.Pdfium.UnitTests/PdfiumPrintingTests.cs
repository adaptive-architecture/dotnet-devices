#nullable enable
using AdaptArch.Devices.Pdfium;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The public surface of <c>AdaptArch.Devices.Pdfium</c>: which formats the converter
/// claims, and what turning it on does to the format policy an application prints through.
/// </summary>
public class PdfiumPrintingTests
{
    [Fact]
    public void PdfConverter_ReadsPdfAndNothingElse()
    {
        var converter = PdfiumPrinting.PdfConverter;

        Assert.True(converter.CanConvert(PrinterContentTypes.Pdf));
        Assert.True(converter.CanConvert("APPLICATION/PDF"));
        Assert.False(converter.CanConvert(PrinterContentTypes.Png));
        Assert.False(converter.CanConvert(PrinterContentTypes.Zpl));
        Assert.False(converter.CanConvert(PrinterContentTypes.PwgRaster));
    }

    [Fact]
    public void PdfConverter_WritesBothFormatsTheTwoChannelsAskFor()
    {
        var converter = PdfiumPrinting.PdfConverter;

        // The spooler draws PNG pages through GDI; an IPP printer reads PWG Raster and
        // never PNG. Claiming both is what makes this package interchangeable with the
        // Windows one rather than complementary to it.
        Assert.True(converter.CanEmit(PrinterContentTypes.Png));
        Assert.True(converter.CanEmit("IMAGE/PWG-RASTER"));
        Assert.True(converter.CanEmit(PrinterContentTypes.PwgRaster));
        Assert.False(converter.CanEmit(PrinterContentTypes.Jpeg));
        Assert.False(converter.CanEmit(PrinterContentTypes.Pdf));
    }

    [Fact]
    public void PdfConverter_IsTheSameInstanceEveryTime() =>
        // EnablePdfPrinting registers it process-wide, so a second instance would be a
        // second registration for the same engine.
        Assert.Same(PdfiumPrinting.PdfConverter, PdfiumPrinting.PdfConverter);

    [Fact]
    public void ManagerOptions_TakeTheConverterWithoutTouchingTheProcess()
    {
        PrinterManagerOptions options = new();
        options.Converters.Add(PdfiumPrinting.PdfConverter);

        var policy = options.BuildFormatPolicy();

        // The scoped way in: this manager converts PDF and no other manager changes.
        Assert.Same(PdfiumPrinting.PdfConverter, policy.ConverterFor(PrinterContentTypes.Pdf));
        Assert.Null(PrintFormatPolicy.Default.ConverterFor(PrinterContentTypes.Zpl));
    }

    [Fact]
    public void ManagerOptions_WithoutTheConverter_ConvertNothing() =>
        // Without it a PDF job fails rather than spooling silence, which is the behaviour
        // EnablePdfPrinting exists to change.
        Assert.Null(new PrinterManagerOptions().BuildFormatPolicy().ConverterFor(PrinterContentTypes.Pdf));
}
