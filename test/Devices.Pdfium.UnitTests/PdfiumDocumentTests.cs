#nullable enable
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The public render entry point: what an application places a page with, and what the
/// built-in converter itself renders through.
/// </summary>
public class PdfiumDocumentTests
{
    [Fact]
    public async Task RenderAsync_CarriesThePageBoxInPoints()
    {
        // The page box is what a job that takes its media from the document is measured in,
        // and it is the one thing a pixel count cannot say.
        var pages = await PdfiumDocument.RenderAsync(
            TestPdf.Barcode(),
            new PdfRenderOptions { Dpi = 150 },
            TestContext.Current.CancellationToken);

        var page = Assert.Single(pages);
        Assert.Equal(288, page.WidthPoints);
        Assert.Equal(144, page.HeightPoints);

        // Four inches by two at 150 dots an inch.
        Assert.Equal(600, page.Width);
        Assert.Equal(300, page.Height);
        Assert.Equal(3, page.BytesPerPixel);
    }

    [Fact]
    public async Task RenderAsync_Grayscale_IsOneOctetAPixel()
    {
        var pages = await PdfiumDocument.RenderAsync(
            TestPdf.Barcode(),
            new PdfRenderOptions { Dpi = 150, ColorSpace = PwgRasterColorSpace.Grayscale8 },
            TestContext.Current.CancellationToken);

        var page = Assert.Single(pages);
        Assert.Equal(1, page.BytesPerPixel);
        Assert.Equal(page.Width * page.Height, page.Pixels.Length);
    }

    [Fact]
    public async Task RenderAsync_APasswordProtectedFile_OpensWithTheRightPassword()
    {
        var pages = await PdfiumDocument.RenderAsync(
            EncryptedPdf.OnePage("secret"),
            new PdfRenderOptions { Dpi = 150, Password = "secret" },
            TestContext.Current.CancellationToken);

        // One inch square at 150 dots an inch, and the page is filled with black, so the
        // password really did open the content stream and not only the catalogue.
        var page = Assert.Single(pages);
        Assert.Equal(150, page.Width);
        Assert.All(page.Pixels, octet => Assert.Equal(0, octet));
    }

    [Fact]
    public async Task RenderAsync_APasswordProtectedFile_SaysSoWhenTheJobCarriedNone()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PdfiumDocument.RenderAsync(
                EncryptedPdf.OnePage("secret"),
                new PdfRenderOptions { Dpi = 150 },
                TestContext.Current.CancellationToken));

        // The caller can act on this one, which is the whole point of telling it apart from a
        // corrupt file.
        Assert.Contains("carried no password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAsync_AWrongPassword_SaysThatAndNotThatTheFileIsBroken()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PdfiumDocument.RenderAsync(
                EncryptedPdf.OnePage("secret"),
                new PdfRenderOptions { Dpi = 150, Password = "wrong" },
                TestContext.Current.CancellationToken));

        Assert.Contains("does not open", exception.Message, StringComparison.Ordinal);

        // The password is a secret: it must not be in the message that goes to a log.
        Assert.DoesNotContain("wrong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_ThroughTheConverter_TakesThePasswordFromTheJob()
    {
        PrintConversionContext context = new(PrinterContentTypes.Pdf, PrinterContentTypes.Png, 150, null, "queue")
        {
            DocumentPassword = "secret",
        };

        var pages = await PdfiumPrinting.PdfConverter.ConvertAsync(
            EncryptedPdf.OnePage("secret"), context, TestContext.Current.CancellationToken);

        _ = Assert.Single(pages);
    }
}
