#nullable enable
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Windows;
using Xunit;

namespace AdaptArch.Devices.Windows.UnitTests;

/// <summary>
/// The public surface of <c>AdaptArch.Devices.Windows</c>.
/// </summary>
/// <remarks>
/// Rendering a PDF needs the in-box engine and runs on Windows only, and
/// <see cref="docs/windows-manual-tests.md"/> covers that. What answers here is everything
/// around it: which formats the converter claims, and what turning it on does to the format
/// policy an application prints through.
/// </remarks>
public class WindowsPrintingTests
{
    [Fact]
    public void PdfConverter_ReadsPdfAndNothingElse()
    {
        var converter = WindowsPrinting.PdfConverter;

        Assert.True(converter.CanConvert(PrinterContentTypes.Pdf));
        Assert.True(converter.CanConvert("APPLICATION/PDF"));
        Assert.False(converter.CanConvert(PrinterContentTypes.Png));
        Assert.False(converter.CanConvert(PrinterContentTypes.Zpl));
        Assert.False(converter.CanConvert(PrinterContentTypes.PwgRaster));
    }

    [Fact]
    public void PdfConverter_WritesBothFormatsTheTwoChannelsAskFor()
    {
        var converter = WindowsPrinting.PdfConverter;

        // The spooler draws PNG pages through GDI; an IPP printer reads PWG Raster and
        // never PNG. A converter that claimed only one would leave a channel unreachable.
        Assert.True(converter.CanEmit(PrinterContentTypes.Png));
        Assert.True(converter.CanEmit(PrinterContentTypes.PwgRaster));
        Assert.False(converter.CanEmit(PrinterContentTypes.Jpeg));
        Assert.False(converter.CanEmit(PrinterContentTypes.Pdf));
    }

    [Fact]
    public void PdfConverter_IsTheSameInstanceEveryTime()
    {
        // EnablePdfPrinting registers it process-wide, so a second instance would be a
        // second registration for the same engine.
        Assert.Same(WindowsPrinting.PdfConverter, WindowsPrinting.PdfConverter);
    }

    [Fact]
    public async Task ConvertAsync_RejectsAMissingContext() =>
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => WindowsPrinting.PdfConverter.ConvertAsync([1, 2, 3], null!, TestContext.Current.CancellationToken));

    [Fact]
    public void ManagerOptions_TakeTheConverterWithoutTouchingTheProcess()
    {
        PrinterManagerOptions options = new();
        options.Converters.Add(WindowsPrinting.PdfConverter);

        var policy = options.BuildFormatPolicy();

        // The scoped way in: this manager converts PDF and no other manager changes.
        Assert.Same(WindowsPrinting.PdfConverter, policy.ConverterFor(PrinterContentTypes.Pdf));
        Assert.Null(PrintFormatPolicy.Default.ConverterFor(PrinterContentTypes.Zpl));
    }

    [Fact]
    public void ManagerOptions_WithoutTheConverter_ConvertNothing()
    {
        PrinterManagerOptions options = new();

        // Without it a PDF job fails rather than spooling silence, which is the behaviour
        // EnablePdfPrinting exists to change.
        Assert.Null(options.BuildFormatPolicy().ConverterFor(PrinterContentTypes.Pdf));
    }
}
