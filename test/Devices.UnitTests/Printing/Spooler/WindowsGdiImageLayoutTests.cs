using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// No P/Invoke: the layout only turns sizes and options into a rectangle.
public class WindowsGdiImageLayoutTests
{
    [Fact]
    public void Compute_FitKeepsAspectAndCenters()
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.Fit);

        Assert.Equal(1000, layout.Width);
        Assert.Equal(500, layout.Height);
        Assert.Equal(0, layout.X);
        Assert.Equal(150, layout.Y);
    }

    [Fact]
    public void Compute_FillCoversPageAndCenters()
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.Fill);

        Assert.Equal(1600, layout.Width);
        Assert.Equal(800, layout.Height);
        Assert.Equal(-300, layout.X);
        Assert.Equal(0, layout.Y);
    }

    [Fact]
    public void Compute_NoneKeepsNaturalSize()
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.None);

        Assert.Equal(400, layout.Width);
        Assert.Equal(200, layout.Height);
        Assert.Equal(300, layout.X);
        Assert.Equal(300, layout.Y);
    }

    [Fact]
    public void Compute_AutoFitKeepsSmallImagesAtNaturalSize()
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.AutoFit);

        Assert.Equal(400, layout.Width);
        Assert.Equal(200, layout.Height);
    }

    [Fact]
    public void Compute_AutoFitScalesLargeImagesDown()
    {
        var layout = WindowsGdiImageLayout.Compute(2000, 1600, 1000, 800, PrintOrientation.Portrait, PrintScaling.AutoFit);

        Assert.Equal(1000, layout.Width);
        Assert.Equal(800, layout.Height);
    }

    // The fit judges the footprint the turned image leaves on the page: 400x800 here.
    // The rectangle is stated in the turned frame, where the image keeps its own axes,
    // so the sides come back as 800x400.
    [Fact]
    public void Compute_LandscapeFitsTheTurnedFootprintButKeepsImageAxes()
    {
        var landscape = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Landscape, PrintScaling.Fit);

        Assert.Equal(800, landscape.Width);
        Assert.Equal(400, landscape.Height);
        Assert.Equal(100, landscape.X);
        Assert.Equal(200, landscape.Y);
    }

    // A turned image must never be squeezed: the drawn aspect ratio stays the image one.
    [Theory]
    [InlineData(PrintOrientation.Portrait)]
    [InlineData(PrintOrientation.Landscape)]
    [InlineData(PrintOrientation.ReverseLandscape)]
    [InlineData(PrintOrientation.ReversePortrait)]
    public void Compute_KeepsTheImageAspectRatio(PrintOrientation orientation)
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, orientation, PrintScaling.Fit);

        Assert.Equal(2.0, (double)layout.Width / layout.Height, 3);
    }

    // The rotation turns around the page center, so the rectangle must be centered there.
    [Theory]
    [InlineData(PrintOrientation.Landscape)]
    [InlineData(PrintOrientation.ReverseLandscape)]
    public void Compute_CentersSidewaysOnThePageCenter(PrintOrientation orientation)
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, orientation, PrintScaling.Fit);

        Assert.Equal(1000, (2 * layout.X) + layout.Width);
        Assert.Equal(800, (2 * layout.Y) + layout.Height);
    }

    [Fact]
    public void Compute_SidewaysNoneKeepsNaturalSize()
    {
        var layout = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.ReverseLandscape, PrintScaling.None);

        Assert.Equal(400, layout.Width);
        Assert.Equal(200, layout.Height);
        Assert.Equal(300, layout.X);
        Assert.Equal(300, layout.Y);
    }

    // 300 image dots an inch written at 600 device dots an inch is twice the pixels,
    // and the same number of inches on paper.
    [Theory]
    [InlineData(600, 300, 600, 1200)]
    [InlineData(600, 600, 600, 600)]
    [InlineData(600, 1200, 600, 300)]
    [InlineData(600, 96, 96, 600)]
    public void NaturalPixels_ReadsTheSourceAndWritesTheDevice(
        int imagePixels, double sourceDpi, int deviceDpi, int expected)
    {
        Assert.Equal(expected, WindowsGdiImageLayout.NaturalPixels(imagePixels, sourceDpi, deviceDpi));
    }

    // A resolution no one stated leaves the pixel count alone, which is what the old
    // behaviour was, rather than making the image vanish.
    [Theory]
    [InlineData(600, 0, 600)]
    [InlineData(600, 300, 0)]
    public void NaturalPixels_KeepsThePixelsWhenAResolutionIsMissing(
        int imagePixels, double sourceDpi, int deviceDpi)
    {
        Assert.Equal(imagePixels, WindowsGdiImageLayout.NaturalPixels(imagePixels, sourceDpi, deviceDpi));
    }

    // The point of the natural size: at its own size a 2 inch image covers 2 inches of
    // the page, whatever the device resolution is.
    [Theory]
    [InlineData(300)]
    [InlineData(600)]
    public void Compute_NoneCoversTheSameInchesOnEveryDevice(int deviceDpi)
    {
        var natural = WindowsGdiImageLayout.NaturalPixels(600, 300, deviceDpi);
        var layout = WindowsGdiImageLayout.Compute(natural, natural, 8 * deviceDpi, 10 * deviceDpi, null, PrintScaling.None);

        Assert.Equal(2.0, (double)layout.Width / deviceDpi, 2);
        Assert.Equal(2.0, (double)layout.Height / deviceDpi, 2);
    }

    [Fact]
    public void Compute_EmptyWhenAnySideIsMissing()
    {
        Assert.True(WindowsGdiImageLayout.Compute(0, 200, 1000, 800, null, null).IsEmpty);
        Assert.True(WindowsGdiImageLayout.Compute(400, 200, 0, 800, null, null).IsEmpty);
    }

    [Theory]
    [InlineData(PrintOrientation.Portrait, 0)]
    [InlineData(PrintOrientation.Landscape, -90)]
    [InlineData(PrintOrientation.ReverseLandscape, 90)]
    [InlineData(PrintOrientation.ReversePortrait, 180)]
    public void RotationDegrees_MatchesIppCounterClockwise(PrintOrientation orientation, float expected)
    {
        Assert.Equal(expected, WindowsGdiImageLayout.RotationDegrees(orientation));
    }

    [Fact]
    public void RotationDegrees_NullIsPortrait()
    {
        Assert.Equal(0f, WindowsGdiImageLayout.RotationDegrees(null));
    }
}
