#nullable enable
using AdaptArch.Devices.Windows;
using Xunit;

namespace AdaptArch.Devices.Windows.UnitTests;

/// <summary>
/// The band the in-box engine renders well, and the page size that comes out of it.
/// </summary>
public class WindowsPdfLimitsTests
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
        Assert.Equal(expected, WindowsPdfLimits.ClampDpi(asked));

    [Fact]
    public void RenderPixels_TurnsDeviceIndependentPixelsIntoDots()
    {
        // PdfPage.Size answers in device-independent pixels of 1/96 inch, not in the
        // points a PDF is authored in. A4 is 8.27x11.69 inches, so 794x1123 of them, and
        // at 300 dots an inch that is 2482x3510.
        var (width, height) = WindowsPdfLimits.RenderPixels(794, 1123, 300);

        Assert.Equal(2482u, width);
        Assert.Equal(3510u, height);

        // Half the resolution, half the page: the engine is asked for dots, not for a size.
        var (halfWidth, halfHeight) = WindowsPdfLimits.RenderPixels(794, 1123, 150);
        Assert.Equal(1241u, halfWidth);
        Assert.Equal(1755u, halfHeight);
    }

    [Fact]
    public void RenderPixels_APageOverTheCap_KeepsItsShape()
    {
        // A0 at 600 dots an inch is far over the cap. Capping each side on its own would
        // make it square; the factor belongs to the longer side and both sides take it.
        var (width, height) = WindowsPdfLimits.RenderPixels(2384, 3370, 600);

        Assert.True(width <= WindowsPdfLimits.MaxRenderPixels);
        Assert.True(height <= WindowsPdfLimits.MaxRenderPixels);
        Assert.Equal(WindowsPdfLimits.MaxRenderPixels, height);

        const double askedRatio = 2384d / 3370d;
        var gotRatio = (double)width / height;
        Assert.True(Math.Abs(askedRatio - gotRatio) < 0.01, $"{askedRatio} against {gotRatio}");
    }

    [Fact]
    public void RenderPixels_ALandscapePageOverTheCap_CapsTheWidthInstead()
    {
        var (width, height) = WindowsPdfLimits.RenderPixels(3370, 2384, 600);

        Assert.Equal(WindowsPdfLimits.MaxRenderPixels, width);
        Assert.True(height < WindowsPdfLimits.MaxRenderPixels);
    }

    [Fact]
    public void RenderPixels_APageTooSmallToMeasure_StillRendersAPixel()
    {
        // A page of no size renders nothing at all, and the encoder behind this reports
        // that as a corrupt file rather than as an empty page.
        var (width, height) = WindowsPdfLimits.RenderPixels(0.01, 0.01, 150);

        Assert.Equal(1u, width);
        Assert.Equal(1u, height);
    }

    [Fact]
    public void RenderPixels_AtTheCapExactly_DoesNotPassIt()
    {
        // The round up can pass the cap by one when the scale lands on it exactly.
        const double sideDips = WindowsPdfLimits.MaxRenderPixels * WindowsPdfLimits.DipsPerInch / WindowsPdfLimits.MaxDpi;

        var (width, height) = WindowsPdfLimits.RenderPixels(sideDips, sideDips, WindowsPdfLimits.MaxDpi);

        Assert.Equal(WindowsPdfLimits.MaxRenderPixels, width);
        Assert.Equal(WindowsPdfLimits.MaxRenderPixels, height);
    }
}
