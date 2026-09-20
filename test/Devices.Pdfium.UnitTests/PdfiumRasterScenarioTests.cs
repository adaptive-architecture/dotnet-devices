#nullable enable
using AdaptArch.Devices.Pdfium;
using AdaptArch.Devices.Rasterization;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The four cases of the sample's PDF job set, rendered by PDFium.
/// </summary>
/// <remarks>
/// PDFium is a native library this package carries for every platform, so unlike the in-box
/// Windows engine these run wherever the tests run, CI included. The PNGs land under
/// <c>artifacts/rasterization/PDFium/</c>.
/// </remarks>
public class PdfiumRasterScenarioTests
{
    [Fact]
    public async Task EveryScenario_RendersWhatThePrinterWouldHaveHadToShow()
    {
        var sheet = await RasterScenarios.RunAsync(
            PdfiumPrinting.PdfConverter,
            PdfiumPrinting.PdfConverter.Name,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(sheet), $"No contact sheet at {sheet}.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Rendered pages: {sheet}");
    }
}
