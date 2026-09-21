#nullable enable
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

/// <summary>
/// The band a PDF engine renders well, and the page size that comes out of it. One suite,
/// because both engines size a page through the same arithmetic and differ only in the unit
/// they measure the page box in.
/// </summary>
public class PdfRenderLimitsTests
{
    [Theory]
    [InlineData(0, 150)]
    [InlineData(-1, 150)]
    [InlineData(72, 150)]
    [InlineData(150, 150)]
    [InlineData(300, 300)]
    [InlineData(600, 600)]
    [InlineData(1200, 600)]
    [InlineData(Int32.MaxValue, 600)]
    public void ClampDpi_KeepsTheResolutionInsideWhatTheEngineRendersWell(int asked, int expected) =>
        Assert.Equal(expected, PdfRenderLimits.ClampDpi(asked));

    [Fact]
    public void RenderPixels_TurnsPointsIntoDots()
    {
        // PDFium answers in points of 1/72 inch, which is the unit a PDF is authored in.
        // US Letter is 612x792 of them, so 8.5x11 inches, and at 300 dots an inch that is
        // 2550x3300.
        var (width, height) = PdfRenderLimits.RenderPixels(612, 792, 300, PdfRenderLimits.PointsPerInch);

        Assert.Equal(2550, width);
        Assert.Equal(3300, height);

        // Half the resolution, half the page: the engine is asked for dots, not for a size.
        var (halfWidth, halfHeight) = PdfRenderLimits.RenderPixels(612, 792, 150, PdfRenderLimits.PointsPerInch);
        Assert.Equal(1275, halfWidth);
        Assert.Equal(1650, halfHeight);
    }

    [Fact]
    public void RenderPixels_TurnsDeviceIndependentPixelsIntoDots()
    {
        // The in-box Windows engine answers in device-independent pixels of 1/96 inch. A4 is
        // 8.27x11.69 inches, so 794x1123 of them, and at 300 dots an inch that is 2482x3510.
        var (width, height) = PdfRenderLimits.RenderPixels(794, 1123, 300, PdfRenderLimits.DipsPerInch);

        Assert.Equal(2482, width);
        Assert.Equal(3510, height);

        var (halfWidth, halfHeight) = PdfRenderLimits.RenderPixels(794, 1123, 150, PdfRenderLimits.DipsPerInch);
        Assert.Equal(1241, halfWidth);
        Assert.Equal(1755, halfHeight);
    }

    [Fact]
    public void RenderPixels_TheSamePageInEitherUnit_ComesOutTheSameSize()
    {
        // The one thing two engines must agree on: US Letter is US Letter whether it arrives
        // as 612x792 points or as 816x1056 device-independent pixels.
        var points = PdfRenderLimits.RenderPixels(612, 792, 300, PdfRenderLimits.PointsPerInch);
        var dips = PdfRenderLimits.RenderPixels(816, 1056, 300, PdfRenderLimits.DipsPerInch);

        Assert.Equal(points, dips);
    }

    [Fact]
    public void RenderPixels_APageOverTheCap_KeepsItsShape()
    {
        // A0 at 600 dots an inch is far over the cap. Capping each side on its own would
        // make it square; the factor belongs to the longer side and both sides take it.
        var (width, height) = PdfRenderLimits.RenderPixels(2384, 3370, 600, PdfRenderLimits.PointsPerInch);

        Assert.True(width <= PdfRenderLimits.MaxRenderPixels);
        Assert.Equal(PdfRenderLimits.MaxRenderPixels, height);

        const double AskedRatio = 2384d / 3370d;
        var gotRatio = (double)width / height;
        Assert.True(Math.Abs(gotRatio - AskedRatio) < 0.001, $"the page changed shape: {gotRatio} against {AskedRatio}");
    }

    [Fact]
    public void RenderPixels_ALandscapePageOverTheCap_CapsTheWidthInstead()
    {
        var (width, height) = PdfRenderLimits.RenderPixels(3370, 2384, 600, PdfRenderLimits.PointsPerInch);

        Assert.Equal(PdfRenderLimits.MaxRenderPixels, width);
        Assert.True(height < PdfRenderLimits.MaxRenderPixels);
    }

    [Fact]
    public void RenderPixels_APageTooSmallToMeasure_StillRendersAPixel()
    {
        // A bitmap of no size is one an engine refuses to allocate, and the renderer would
        // report that as a page it could not read.
        var (width, height) = PdfRenderLimits.RenderPixels(0.01, 0.01, 150, PdfRenderLimits.PointsPerInch);

        Assert.Equal(1, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public void RenderPixels_AtTheCapExactly_DoesNotPassIt()
    {
        // The round up can pass the cap by one when the scale lands on it exactly.
        const double Side = PdfRenderLimits.MaxRenderPixels * PdfRenderLimits.PointsPerInch / PdfRenderLimits.MaxDpi;

        var (width, height) = PdfRenderLimits.RenderPixels(Side, Side, PdfRenderLimits.MaxDpi, PdfRenderLimits.PointsPerInch);

        Assert.Equal(PdfRenderLimits.MaxRenderPixels, width);
        Assert.Equal(PdfRenderLimits.MaxRenderPixels, height);
    }
}
