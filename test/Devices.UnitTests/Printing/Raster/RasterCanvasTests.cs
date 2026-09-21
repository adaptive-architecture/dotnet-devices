using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

// Composition, clipping and the two resamplings. Every page here is small enough to read
// pixel by pixel, because that is the only way to see where a page actually landed.
public class RasterCanvasTests
{
    private const int Black = 0x00;
    private const int White = 0xFF;

    [Fact]
    public void Compose_PutsThePageWhereTheRectangleSays()
    {
        // One black pixel, drawn at its own size three across and two down.
        var canvas = Compose([Black], 1, 1, 1, 5, 4, new ImageRectangle(3, 2, 1, 1), RasterResampling.NearestNeighbor);

        Assert.Equal(20, canvas.Length);
        Assert.Equal(Black, canvas[(2 * 5) + 3]);

        // Everything else is the stock.
        for (var i = 0; i < canvas.Length; i++)
        {
            if (i != (2 * 5) + 3)
            {
                Assert.Equal(White, canvas[i]);
            }
        }
    }

    [Fact]
    public void Compose_LeavesTheRestOfTheMediaWhite()
    {
        var canvas = Compose([Black], 1, 1, 1, 3, 3, ImageRectangle.Empty, RasterResampling.Bilinear);

        Assert.All(canvas, octet => Assert.Equal(White, octet));
    }

    [Fact]
    public void Compose_ClipsAPageThatStartsOutsideTheMedia()
    {
        // A two-by-two black page whose top left corner sits one pixel off the media: only
        // the quarter of it that lands on the canvas is drawn, and nothing throws.
        byte[] page = [Black, Black, Black, Black];
        var canvas = Compose(page, 2, 2, 1, 3, 3, new ImageRectangle(-1, -1, 2, 2), RasterResampling.NearestNeighbor);

        Assert.Equal(Black, canvas[0]);
        Assert.Equal(White, canvas[1]);
        Assert.Equal(White, canvas[3]);
    }

    [Fact]
    public void Compose_ClipsAPageThatRunsOffTheFarEdge()
    {
        byte[] page = [Black, Black, Black, Black];
        var canvas = Compose(page, 2, 2, 1, 3, 3, new ImageRectangle(2, 2, 2, 2), RasterResampling.NearestNeighbor);

        Assert.Equal(Black, canvas[(2 * 3) + 2]);
        Assert.Equal(White, canvas[0]);
    }

    [Theory]
    [InlineData(RasterResampling.NearestNeighbor)]
    [InlineData(RasterResampling.Bilinear)]
    public void Compose_CopiesAPageDrawnAtItsOwnSizeUnchanged(RasterResampling resampling)
    {
        byte[] page = [0x10, 0x20, 0x30, 0x40];
        var canvas = Compose(page, 2, 2, 1, 2, 2, new ImageRectangle(0, 0, 2, 2), resampling);

        Assert.Equal(page, canvas);
    }

    [Fact]
    public void Compose_NearestNeighborKeepsAnEdgeHard()
    {
        // One black pixel beside one white one, drawn four times as wide. Nearest keeps every
        // pixel one or the other, which is what a bar code needs.
        byte[] page = [Black, White];
        var canvas = Compose(page, 2, 1, 1, 8, 1, new ImageRectangle(0, 0, 8, 1), RasterResampling.NearestNeighbor);

        Assert.All(canvas, octet => Assert.True(octet is Black or White, $"{octet} is neither black nor white."));
        Assert.Equal(Black, canvas[0]);
        Assert.Equal(White, canvas[7]);
    }

    [Fact]
    public void Compose_BilinearRampsAcrossAnEdge()
    {
        byte[] page = [Black, White];
        var canvas = Compose(page, 2, 1, 1, 8, 1, new ImageRectangle(0, 0, 8, 1), RasterResampling.Bilinear);

        // The ramp is the whole difference between the two: some pixel in the middle is
        // neither, which is exactly what smoothing off exists to prevent.
        Assert.Contains(canvas, octet => octet is not Black and not White);
        Assert.Equal(Black, canvas[0]);
        Assert.Equal(White, canvas[7]);
    }

    [Fact]
    public void Compose_ReadsThreeOctetsAPixel()
    {
        byte[] page = [0x11, 0x22, 0x33];
        var canvas = Compose(page, 1, 1, 3, 2, 1, new ImageRectangle(1, 0, 1, 1), RasterResampling.NearestNeighbor);

        Assert.Equal(6, canvas.Length);
        Assert.Equal(0x11, canvas[3]);
        Assert.Equal(0x22, canvas[4]);
        Assert.Equal(0x33, canvas[5]);

        // The pixel the page did not cover is white in all three channels.
        Assert.Equal(White, canvas[0]);
        Assert.Equal(White, canvas[1]);
        Assert.Equal(White, canvas[2]);
    }

    [Fact]
    public void Compose_RefusesASizeItCannotRead()
    {
        byte[] page = [Black];

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => Compose(page, 0, 1, 1, 2, 2, new ImageRectangle(0, 0, 1, 1), RasterResampling.Bilinear));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => Compose(page, 1, 1, 2, 2, 2, new ImageRectangle(0, 0, 1, 1), RasterResampling.Bilinear));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => Compose(page, 4, 4, 1, 2, 2, new ImageRectangle(0, 0, 1, 1), RasterResampling.Bilinear));
    }

    [Fact]
    public void Place_AnswersNothingWhenTheChannelNamedNoMedia()
    {
        PrintConversionContext context = new(PrinterContentTypes.Pdf, PrinterContentTypes.Png, 300, null, "queue");

        Assert.Null(RasterPlacement.Place([Black], 1, 1, 1, context));
    }

    [Fact]
    public void Place_AnswersNothingWhenTheMediaIsTheDocument()
    {
        PrintConversionContext context = new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, 300, null, "queue")
        {
            MediaWidthPixels = 100,
            MediaHeightPixels = 100,
            MediaSizeSource = MediaSizeSource.Document,
        };

        Assert.Null(RasterPlacement.Place([Black], 1, 1, 1, context));
    }

    [Fact]
    public void Place_AnswersNothingWhenThePageAlreadyCoversItsMedia()
    {
        PrintConversionContext context = new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, 300, null, "queue")
        {
            MediaWidthPixels = 2,
            MediaHeightPixels = 2,
            Scaling = PrintScaling.None,
        };

        Assert.Null(RasterPlacement.Place([Black, Black, Black, Black], 2, 2, 1, context));
    }

    [Fact]
    public void Place_PutsTheOffsetIntoThePixels()
    {
        PrintConversionContext context = new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, 2540, null, "queue")
        {
            MediaWidthPixels = 10,
            MediaHeightPixels = 10,
            Scaling = PrintScaling.None,
            Placement = new PrintPlacement
            {
                Anchor = PrintAnchor.TopLeft,

                // At 2540 dots an inch, one hundredth of a millimetre is one pixel.
                OffsetX = PrintLength.FromHundredthsOfMillimeter(3),
                OffsetY = PrintLength.FromHundredthsOfMillimeter(2),
            },
        };

        var placed = RasterPlacement.Place([Black], 1, 1, 1, context);

        Assert.NotNull(placed);
        Assert.Equal(10, placed.Width);
        Assert.Equal(10, placed.Height);
        Assert.Equal(Black, placed.Pixels[(2 * 10) + 3]);
    }

    [Theory]
    [InlineData(null, RasterResampling.Bilinear)]
    [InlineData(true, RasterResampling.Bilinear)]
    [InlineData(false, RasterResampling.NearestNeighbor)]
    public void ResamplingFor_ReadsTheSmoothingSwitch(bool? smoothing, RasterResampling expected) =>
        Assert.Equal(expected, RasterPlacement.ResamplingFor(smoothing));

    // The positional shape the assertions below read best in. RasterCanvas takes the canvas,
    // the destination and the resampling as one target, which is right for a caller that
    // builds them from a job and noise in a test that states them as numbers.
    private static byte[] Compose(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        int canvasWidth,
        int canvasHeight,
        ImageRectangle destination,
        RasterResampling resampling) =>
        RasterCanvas.Compose(pixels, width, height, bytesPerPixel, new RasterTarget
        {
            Width = canvasWidth,
            Height = canvasHeight,
            Destination = destination,
            Resampling = resampling,
        });
}
