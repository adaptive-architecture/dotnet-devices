using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

// What the channel has to know once a converter has placed a page, and which rectangle the
// page was placed inside.
public class RasterPlacementTests
{
    private const int Dpi = 2540; // One hundredth of a millimetre is one pixel, so the arithmetic reads plainly.

    [Fact]
    public void PlacesOnMedia_IsFalseWhenTheChannelNamedNoMedia() =>
        Assert.False(RasterPlacement.PlacesOnMedia(Context()));

    [Fact]
    public void PlacesOnMedia_IsTrueWhenTheChannelNamedTheMedia() =>
        Assert.True(RasterPlacement.PlacesOnMedia(Context() with { MediaWidthPixels = 100, MediaHeightPixels = 100 }));

    [Fact]
    public void PlacesOnMedia_IsTrueWhenTheMediaIsTheDocument() =>
        Assert.True(RasterPlacement.PlacesOnMedia(Context() with { MediaSizeSource = MediaSizeSource.Document }));

    [Fact]
    public void Place_AnchorsInsideTheFitArea()
    {
        // The media is 100 square with a 10-pixel margin all round. A page anchored to the
        // top left of what the printer can mark starts at the margin, not at the sheet.
        var context = Context() with
        {
            MediaWidthPixels = 100,
            MediaHeightPixels = 100,
            FitArea = new ImageRectangle(10, 10, 80, 80),
            Scaling = PrintScaling.None,
            Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft },
        };

        var placed = RasterPlacement.Place([0x00], 1, 1, 1, context);

        Assert.NotNull(placed);
        Assert.Equal(0xFF, placed.Pixels[(9 * 100) + 9]);
        Assert.Equal(0x00, placed.Pixels[(10 * 100) + 10]);
    }

    [Fact]
    public void Place_FitsInsideTheFitAreaAndNotTheSheet()
    {
        // A page larger than the printable area is fitted to that area, so the margin stays
        // clear. Fitted to the sheet it would cover the margin and be clipped by the hardware.
        var context = Context() with
        {
            MediaWidthPixels = 100,
            MediaHeightPixels = 100,
            FitArea = new ImageRectangle(10, 10, 80, 80),
            Scaling = PrintScaling.Fit,
        };

        var placed = RasterPlacement.Place(new byte[200 * 200], 200, 200, 1, context);

        Assert.NotNull(placed);
        Assert.Equal(0xFF, placed.Pixels[(9 * 100) + 50]);
        Assert.Equal(0x00, placed.Pixels[(11 * 100) + 50]);
    }

    [Fact]
    public void Place_WithoutAFitAreaUsesTheWholeSheet()
    {
        var context = Context() with
        {
            MediaWidthPixels = 100,
            MediaHeightPixels = 100,
            Scaling = PrintScaling.None,
            Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft },
        };

        var placed = RasterPlacement.Place([0x00], 1, 1, 1, context);

        Assert.NotNull(placed);
        Assert.Equal(0x00, placed.Pixels[0]);
    }

    [Fact]
    public void Place_OffsetIsMeasuredFromTheFitArea()
    {
        var context = Context() with
        {
            MediaWidthPixels = 100,
            MediaHeightPixels = 100,
            FitArea = new ImageRectangle(10, 10, 80, 80),
            Scaling = PrintScaling.None,
            Placement = new PrintPlacement
            {
                Anchor = PrintAnchor.TopLeft,
                OffsetX = PrintLength.FromHundredthsOfMillimeter(5),
                OffsetY = PrintLength.FromHundredthsOfMillimeter(2),
            },
        };

        var placed = RasterPlacement.Place([0x00], 1, 1, 1, context);

        Assert.NotNull(placed);
        Assert.Equal(0x00, placed.Pixels[((10 + 2) * 100) + 10 + 5]);
    }

    private static PrintConversionContext Context() =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, Dpi, null, "queue");
}
