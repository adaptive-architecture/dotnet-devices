#nullable enable
using AdaptArch.Devices.Pdfium;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The band PDFium renders well, and the page size that comes out of it.
/// </summary>
public class PdfiumLimitsTests
{
    [Theory]
    [InlineData(0, 150)]
    [InlineData(72, 150)]
    [InlineData(149, 150)]
    [InlineData(150, 150)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(1200, 600)]
    [InlineData(Int32.MaxValue, 600)]
    public void ClampDpi_KeepsTheResolutionInsideWhatTheEngineRendersWell(int asked, int expected) =>
        Assert.Equal(expected, PdfiumLimits.ClampDpi(asked));

    [Fact]
    public void RenderPixels_TurnsPointsIntoDots()
    {
        // PDFium answers in points of 1/72 inch, which is the unit a PDF is authored in.
        // US Letter is 612x792 of them, so 8.5x11 inches, and at 300 dots an inch that is
        // 2550x3300. The Windows engine counts 1/96 instead, which is the one difference.
        (var width, var height) = PdfiumLimits.RenderPixels(612, 792, 300);

        Assert.Equal(2550, width);
        Assert.Equal(3300, height);

        // Half the resolution, half the page: the engine is asked for dots, not for a size.
        (var halfWidth, var halfHeight) = PdfiumLimits.RenderPixels(612, 792, 150);
        Assert.Equal(1275, halfWidth);
        Assert.Equal(1650, halfHeight);
    }

    [Fact]
    public void RenderPixels_APageOverTheCap_KeepsItsShape()
    {
        // A0 at 600 dots an inch is far over the cap. Capping each side on its own would
        // make it square; the factor belongs to the longer side and both sides take it.
        (var width, var height) = PdfiumLimits.RenderPixels(2384, 3370, 600);

        Assert.True(width <= PdfiumLimits.MaxRenderPixels);
        Assert.Equal(PdfiumLimits.MaxRenderPixels, height);

        const double askedRatio = 2384d / 3370d;
        var gotRatio = (double)width / height;
        Assert.True(Math.Abs(askedRatio - gotRatio) < 0.01, $"{askedRatio} against {gotRatio}");
    }

    [Fact]
    public void RenderPixels_ALandscapePageOverTheCap_CapsTheWidthInstead()
    {
        (var width, var height) = PdfiumLimits.RenderPixels(3370, 2384, 600);

        Assert.Equal(PdfiumLimits.MaxRenderPixels, width);
        Assert.True(height < PdfiumLimits.MaxRenderPixels);
    }

    [Fact]
    public void RenderPixels_APageTooSmallToMeasure_StillRendersAPixel()
    {
        // A bitmap of no size is one PDFium refuses to allocate, and the renderer would
        // report that as a page it could not read.
        (var width, var height) = PdfiumLimits.RenderPixels(0.01, 0.01, 150);

        Assert.Equal(1, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void RenderPixels_AtTheCapExactly_DoesNotPassIt()
    {
        // The round up can pass the cap by one when the scale lands on it exactly.
        const double side = PdfiumLimits.MaxRenderPixels * PdfiumLimits.PointsPerInch / PdfiumLimits.MaxDpi;

        (var width, var height) = PdfiumLimits.RenderPixels(side, side, PdfiumLimits.MaxDpi);

        Assert.Equal(PdfiumLimits.MaxRenderPixels, width);
        Assert.Equal(PdfiumLimits.MaxRenderPixels, height);
    }
}
