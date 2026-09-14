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

    [Fact]
    public void Compute_LandscapeSwapsAxesBeforeFitting()
    {
        var landscape = WindowsGdiImageLayout.Compute(400, 200, 1000, 800, PrintOrientation.Landscape, PrintScaling.Fit);

        Assert.Equal(400, landscape.Width);
        Assert.Equal(800, landscape.Height);
        Assert.Equal(300, landscape.X);
        Assert.Equal(0, landscape.Y);
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
