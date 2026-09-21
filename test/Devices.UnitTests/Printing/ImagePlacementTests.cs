using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

// The anchor and the offset, on top of the fit modes WindowsGdiImageLayoutTests already
// covers. No platform call: it is arithmetic all the way down.
public class ImagePlacementTests
{
    [Fact]
    public void Compute_WithoutAnAnchorCentersLikeBefore()
    {
        var centered = ImagePlacement.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.None);

        Assert.Equal(300, centered.X);
        Assert.Equal(300, centered.Y);
        Assert.Equal(400, centered.Width);
        Assert.Equal(200, centered.Height);
    }

    [Theory]
    [InlineData(PrintAnchor.TopLeft, 0, 0)]
    [InlineData(PrintAnchor.TopCenter, 300, 0)]
    [InlineData(PrintAnchor.TopRight, 600, 0)]
    [InlineData(PrintAnchor.CenterLeft, 0, 300)]
    [InlineData(PrintAnchor.Center, 300, 300)]
    [InlineData(PrintAnchor.CenterRight, 600, 300)]
    [InlineData(PrintAnchor.BottomLeft, 0, 600)]
    [InlineData(PrintAnchor.BottomCenter, 300, 600)]
    [InlineData(PrintAnchor.BottomRight, 600, 600)]
    public void Compute_AnchorsAgainstTheEdgesOfTheMedia(PrintAnchor anchor, int expectedX, int expectedY)
    {
        var layout = ImagePlacement.Compute(400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.None, new ImagePlacementOptions { Anchor = anchor });

        Assert.Equal(expectedX, layout.X);
        Assert.Equal(expectedY, layout.Y);
    }

    [Fact]
    public void Compute_OffsetMovesRightAndDownFromTheAnchor()
    {
        var layout = ImagePlacement.Compute(
            400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.None,
            new ImagePlacementOptions { Anchor = PrintAnchor.TopLeft, OffsetX = 24, OffsetY = 12 });

        Assert.Equal(24, layout.X);
        Assert.Equal(12, layout.Y);
    }

    [Fact]
    public void Compute_NegativeOffsetMovesTheOtherWayAndIsAllowedOffTheMedia()
    {
        var layout = ImagePlacement.Compute(
            400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.None,
            new ImagePlacementOptions { Anchor = PrintAnchor.TopLeft, OffsetX = -30, OffsetY = -10 });

        // Negative is how the clipping is expressed: the page starts outside the sheet.
        Assert.Equal(-30, layout.X);
        Assert.Equal(-10, layout.Y);
    }

    [Fact]
    public void Compute_AnchorsTheFittedSizeAndNotTheNaturalOne()
    {
        // Fit scales 400x200 onto 1000x800 as 1000x500, so the bottom edge is what moves.
        var layout = ImagePlacement.Compute(
            400, 200, 1000, 800, PrintOrientation.Portrait, PrintScaling.Fit,
            new ImagePlacementOptions { Anchor = PrintAnchor.BottomLeft });

        Assert.Equal(1000, layout.Width);
        Assert.Equal(500, layout.Height);
        Assert.Equal(0, layout.X);
        Assert.Equal(300, layout.Y);
    }

    [Theory]
    [InlineData(PrintOrientation.Landscape)]
    [InlineData(PrintOrientation.ReverseLandscape)]
    public void Compute_SidewaysLandsWhereTheAnchorSaysOnceItIsTurned(PrintOrientation orientation)
    {
        // The page leaves a 200 by 400 footprint on a 1000 by 800 sheet, and the anchor puts
        // that footprint against the top left of the media. The rectangle answered is in the
        // frame the page is drawn in, which the caller then turns about the centre of the
        // sheet, so what it has to satisfy is that the footprint lands in that corner once
        // turned.
        var layout = ImagePlacement.Compute(
            400, 200, 1000, 800, orientation, PrintScaling.None,
            new ImagePlacementOptions { Anchor = PrintAnchor.TopLeft });

        // Drawn on its own axes: the sides are the page's, not the footprint's.
        Assert.Equal(400, layout.Width);
        Assert.Equal(200, layout.Height);

        (var x, var y) = OnTheMedia(layout, orientation, 1000, 800);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Compute_SidewaysOffsetIsMeasuredOnTheMedia()
    {
        var moved = ImagePlacement.Compute(
            400, 200, 1000, 800, PrintOrientation.Landscape, PrintScaling.None,
            new ImagePlacementOptions { Anchor = PrintAnchor.TopLeft, OffsetX = 30, OffsetY = 10 });

        // An offset is what a person measures on the stock, so it moves the page right and
        // down on the media whichever way the page itself is turned.
        (var x, var y) = OnTheMedia(moved, PrintOrientation.Landscape, 1000, 800);
        Assert.Equal(30, x);
        Assert.Equal(10, y);
    }

    // Where a drawn rectangle ends up on the sheet: the same rotation about the centre of
    // the sheet that WindowsGdiImagePrinter sets on the world transform before it draws.
    private static (int X, int Y) OnTheMedia(ImageRectangle layout, PrintOrientation orientation, int pageWidth, int pageHeight)
    {
        var centerX = layout.X + (layout.Width / 2.0) - (pageWidth / 2.0);
        var centerY = layout.Y + (layout.Height / 2.0) - (pageHeight / 2.0);

        // GDI+ turns by RotationDegrees, which is -90 for landscape and +90 for its reverse.
        var turnedX = centerX;
        var turnedY = centerY;
        if (orientation == PrintOrientation.Landscape)
        {
            (turnedX, turnedY) = (centerY, -centerX);
        }
        else if (orientation == PrintOrientation.ReverseLandscape)
        {
            (turnedX, turnedY) = (-centerY, centerX);
        }
        else if (orientation == PrintOrientation.ReversePortrait)
        {
            (turnedX, turnedY) = (-centerX, -centerY);
        }

        var sideways = ImagePlacement.IsSideways(orientation);
        var footprintWidth = sideways ? layout.Height : layout.Width;
        var footprintHeight = sideways ? layout.Width : layout.Height;
        return (
            (int)Math.Round((pageWidth / 2.0) + turnedX - (footprintWidth / 2.0)),
            (int)Math.Round((pageHeight / 2.0) + turnedY - (footprintHeight / 2.0)));
    }

    [Fact]
    public void Compute_ReversePortraitTurnsTheOffsetAround()
    {
        var moved = ImagePlacement.Compute(
            400, 200, 1000, 800, PrintOrientation.ReversePortrait, PrintScaling.None,
            new ImagePlacementOptions { Anchor = PrintAnchor.TopLeft, OffsetX = 30, OffsetY = 10 });

        // The page is drawn upside down about the centre of the sheet, so the top left of
        // the media is the bottom right of the draw frame.
        Assert.Equal(1000 - 400 - 30, moved.X);
        Assert.Equal(800 - 200 - 10, moved.Y);
    }

    [Fact]
    public void Compute_EmptyWhenASideHasNoSize()
    {
        Assert.True(ImagePlacement.Compute(0, 200, 1000, 800, null, PrintScaling.Fit).IsEmpty);
        Assert.True(ImagePlacement.Compute(400, 200, 1000, 0, null, PrintScaling.Fit).IsEmpty);
    }

    [Fact]
    public void NaturalPixels_ReadsTheSourceResolutionAgainstTheDeviceOne()
    {
        Assert.Equal(600, ImagePlacement.NaturalPixels(300, 150, 300));
        Assert.Equal(300, ImagePlacement.NaturalPixels(300, 0, 300));
    }
}
