#nullable enable
using System.Collections.Generic;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// The four cases of <c>samples/Devices.Samples/PrintJobs/queue-sweep-pdf.json</c>, run
/// through a rasterizer without printing anything.
/// </summary>
/// <remarks>
/// The job set can only be judged on paper. These run the same four cases against the
/// converter directly, assert what a printer would otherwise have to show, and write every
/// page out as a PNG so a person can look at the result.
/// <para>
/// Each engine is measured against itself, never against the other. Not because they
/// disagree about the page size: they do not, and the units each measures it in cancel, so
/// A4 is 1240 by 1755 at 150 dots an inch to both of them. What is left is the rasterizers
/// themselves, which is a difference this repository has no standard to judge. The fixture
/// embeds its font so that the one difference that was never about the engines -- each
/// platform substituting its own Helvetica -- is gone.
/// </para>
/// </remarks>
public static class RasterScenarios
{
    // Inside the band the fixture draws across the top of every page, and clear of the
    // black corner block at the bottom.
    private const double BandSampleY = 0.05;

    // Inside the corner block, which the fixture draws in one corner only.
    private const double CornerSample = 0.02;

    private const int Dpi = 150;

    /// <summary>
    /// Runs every scenario against one converter, asserts, and writes the contact sheet.
    /// </summary>
    /// <param name="converter">The engine under test.</param>
    /// <param name="engine">What to call it on disk. Normally <see cref="IPrintPayloadConverter.Name"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Where the contact sheet was written.</returns>
    public static async Task<string> RunAsync(
        IPrintPayloadConverter converter,
        string engine,
        CancellationToken cancellationToken)
    {
        // The catalogue drives the contact sheet, so a scenario added here without an entry
        // there would render pages nothing ever shows.
        Assert.Contains(RasterCatalogue.Engines, known => known.Name == engine);

        var pdf = RasterDocuments.FourPages();
        var sheet = RasterSheet.For(engine);

        var colour = await RenderAsync(converter, pdf, Context() with { RasterType = "srgb_8" }, cancellationToken);
        AssertColour(colour);
        sheet.Add(RasterCatalogue.Scenarios[0], colour);

        var grey = await RenderAsync(converter, pdf, Context() with { RasterType = "sgray_8" }, cancellationToken);
        AssertGrayscale(grey, colour);
        sheet.Add(RasterCatalogue.Scenarios[1], grey);

        var selected = await RenderAsync(
            converter,
            pdf,
            new PrintConversionContext(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, Dpi, [new PageRange(2, 2), new PageRange(4, 4)], "scenario")
            {
                RasterType = "srgb_8",
            },
            cancellationToken);
        AssertSelected(selected, colour);
        sheet.Add(RasterCatalogue.Scenarios[2], selected);

        var duplex = await RenderAsync(
            converter,
            pdf,
            Context() with { RasterType = "srgb_8", Duplex = DuplexMode.LongEdge, SheetBack = "flipped" },
            cancellationToken);
        AssertDuplex(duplex, colour);
        sheet.Add(RasterCatalogue.Scenarios[3], duplex);

        // The two shapes the sample's "PDF engines" job set prints on each engine. They are
        // the combinations rather than the ingredients: a page range changes which page is a
        // back side, because the transform counts the pages of the converted document and
        // not of the one it came from.
        var frontsInColour = await RenderAsync(
            converter,
            pdf,
            Selecting(1, 3) with { RasterType = "srgb_8", Duplex = DuplexMode.LongEdge, SheetBack = "flipped" },
            cancellationToken);
        AssertSelectedDuplex(frontsInColour, colour, 0, 2, crossFeed: 1, feed: -1);
        sheet.Add(RasterCatalogue.Scenarios[4], frontsInColour);

        var backsInGrey = await RenderAsync(
            converter,
            pdf,
            Selecting(2, 4) with { RasterType = "sgray_8", Duplex = DuplexMode.ShortEdge, SheetBack = "flipped" },
            cancellationToken);
        AssertSelectedDuplex(backsInGrey, grey, 1, 3, crossFeed: -1, feed: 1);
        sheet.Add(RasterCatalogue.Scenarios[5], backsInGrey);

        await RunPlacementAsync(converter, engine, pdf, sheet, colour, cancellationToken);

        return RasterSheet.WriteIndex();
    }

    // Four inches by six at the scenario resolution: the commonest shipping label, and small
    // enough that an A4 page has to be fitted onto it.
    private const int LabelWidthPixels = 4 * Dpi;
    private const int LabelHeightPixels = 6 * Dpi;

    // A4 at the scenario resolution, which is the stock the label is anchored on.
    private const int SheetWidthPixels = 1240;
    private const int SheetHeightPixels = 1755;

    // The placement options, each on one page so the sheet stays readable. Every page here is
    // the first page of the fixture, which carries the barcode every assertion is measured on.
    private static async Task RunPlacementAsync(
        IPrintPayloadConverter converter,
        string engine,
        byte[] pdf,
        RasterSheet sheet,
        IReadOnlyList<PwgRasterReader.RasterPage> colour,
        CancellationToken cancellationToken)
    {
        var fitted = await RenderAsync(
            converter,
            pdf,
            FirstPage() with
            {
                RasterType = "srgb_8",
                MediaWidthPixels = LabelWidthPixels,
                MediaHeightPixels = LabelHeightPixels,
                MediaName = "na_index-4x6_4x6in",
                Scaling = PrintScaling.Fit,
            },
            cancellationToken);
        AssertFitted(Assert.Single(fitted));
        sheet.Add(RasterCatalogue.Scenarios[6], fitted);

        // The label is the sample four by six, which is smaller than the A4 sheet it is put
        // on, so both the corner the anchor names and the inset the offset leaves are
        // visible. It is placed at its own size: a page scaled to get there would be
        // measuring the resampler instead.
        var label = RasterDocuments.Label();
        var loose = await RenderAsync(converter, label, FirstPage() with { RasterType = "srgb_8" }, cancellationToken);
        var anchored = await RenderAsync(
            converter,
            label,
            FirstPage() with
            {
                RasterType = "srgb_8",
                MediaWidthPixels = SheetWidthPixels,
                MediaHeightPixels = SheetHeightPixels,
                MediaName = "iso_a4_210x297mm",
                Scaling = PrintScaling.None,
                Placement = new PrintPlacement
                {
                    Anchor = PrintAnchor.BottomLeft,
                    OffsetX = PrintLength.FromMillimeters(5),
                    OffsetY = PrintLength.FromMillimeters(-3),
                },
            },
            cancellationToken);
        AssertAnchored(Assert.Single(anchored), Assert.Single(loose));
        sheet.Add(RasterCatalogue.Scenarios[7], anchored);

        var smoothed = await RenderAsync(converter, pdf, FirstPage() with { RasterType = "srgb_8" }, cancellationToken);
        var sharp = await RenderAsync(
            converter,
            pdf,
            FirstPage() with { RasterType = "srgb_8", Smoothing = false },
            cancellationToken);
        AssertSmoothing(Assert.Single(smoothed), Assert.Single(sharp), engine);
        sheet.Add(RasterCatalogue.Scenarios[8], smoothed);
        sheet.Add(RasterCatalogue.Scenarios[9], sharp);

        // The media is named and sized, and the document still wins: the page is its own
        // media, so it is neither fitted nor moved.
        var fromDocument = await RenderAsync(
            converter,
            pdf,
            FirstPage() with
            {
                RasterType = "srgb_8",
                MediaWidthPixels = LabelWidthPixels,
                MediaHeightPixels = LabelHeightPixels,
                MediaSizeSource = MediaSizeSource.Document,
            },
            cancellationToken);
        var own = Assert.Single(fromDocument);
        var page = colour[0];
        Assert.Equal(page.Width, own.Width);
        Assert.Equal(page.Height, own.Height);
        Assert.Equal(page.Pixels, own.Pixels);
        sheet.Add(RasterCatalogue.Scenarios[10], fromDocument);
    }

    private static PrintConversionContext FirstPage() =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, Dpi, [new PageRange(1, 1)], "scenario");

    private static void AssertFitted(PwgRasterReader.RasterPage fitted)
    {
        // The printer receives the media, not the page: that is what the composition is for.
        Assert.Equal(LabelWidthPixels, fitted.Width);
        Assert.Equal(LabelHeightPixels, fitted.Height);

        // A4 is narrower than four by six is, so the fit is decided by the width and the
        // page leaves white above and below rather than beside.
        const double Scale = LabelWidthPixels / RasterDocuments.WidthPoints;
        var covered = (int)(RasterDocuments.HeightPoints * Scale);
        Assert.True(covered < LabelHeightPixels, "The fitted page should not fill the label.");

        var margin = (LabelHeightPixels - covered) / 2;
        Assert.True(IsWhite(PixelAt(fitted, LabelWidthPixels / 2, margin / 2)), "The top margin is not the stock.");
        Assert.True(IsWhite(PixelAt(fitted, LabelWidthPixels / 2, LabelHeightPixels - (margin / 2) - 1)), "The bottom margin is not the stock.");

        // The band is the top of the page, so it is inside the fitted rectangle and not in
        // the margin above it.
        Assert.False(IsWhite(PixelAt(fitted, LabelWidthPixels / 2, margin + 10)), "The fitted page did not start below the margin.");
    }

    // The label is anchored against the bottom-left corner of the sheet, and the offset
    // insets it from there: to the right, and up, which is what a negative OffsetY means.
    private static void AssertAnchored(PwgRasterReader.RasterPage anchored, PwgRasterReader.RasterPage label)
    {
        Assert.Equal(SheetWidthPixels, anchored.Width);
        Assert.Equal(SheetHeightPixels, anchored.Height);

        // The label is smaller than the sheet, which is the whole point of anchoring it.
        Assert.True(label.Width < anchored.Width && label.Height < anchored.Height, "The label is not smaller than its sheet.");

        var offsetX = PrintLength.FromMillimeters(5).ToPixels(Dpi);
        var offsetY = PrintLength.FromMillimeters(-3).ToPixels(Dpi);
        var left = offsetX;
        var top = anchored.Height - label.Height + offsetY;

        // Where the ink is, rather than where the paper is: a label is white stock with black
        // on it, so its edges are not visible and only what it draws can be measured. The
        // same label placed at its own size says where that ink belongs, and the difference
        // between the two is the placement and nothing else.
        var placed = InkBounds(anchored);
        var own = InkBounds(label);
        Assert.Equal(own.Left + left, placed.Left);
        Assert.Equal(own.Top + top, placed.Top);
        Assert.Equal(own.Right + left, placed.Right);
        Assert.Equal(own.Bottom + top, placed.Bottom);

        // And nothing of ours reaches the corner the label was moved away from, so the inset
        // is an inset and not a crop.
        Assert.True(IsWhite(PixelAt(anchored, left / 2, anchored.Height - 1)), "The bottom-left corner is not the stock.");
        Assert.True(IsWhite(PixelAt(anchored, anchored.Width - 1, 0)), "The far corner is not the stock.");
    }

    // The smallest rectangle holding every pixel that is not the stock.
    private static (int Left, int Top, int Right, int Bottom) InkBounds(PwgRasterReader.RasterPage page)
    {
        var left = page.Width;
        var top = page.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                if (IsWhite(PixelAt(page, x, y)))
                {
                    continue;
                }

                if (x < left) { left = x; }
                if (x > right) { right = x; }
                if (y < top) { top = y; }
                if (y > bottom) { bottom = y; }
            }
        }

        Assert.True(right >= 0 && bottom >= 0, "The page carries no ink to measure.");
        return (left, top, right, bottom);
    }

    private static void AssertSmoothing(PwgRasterReader.RasterPage smoothed, PwgRasterReader.RasterPage sharp, string engine)
    {
        Assert.Equal(smoothed.Width, sharp.Width);

        var bars = Barcode.BarCount(RasterDocuments.BarcodeDigits(0));
        var withSmoothing = ScanBarcode(smoothed, smoothed.Height, 0);
        var without = ScanBarcode(sharp, sharp.Height, 0);

        // Both read the symbol: the switch changes the edges and never the bars.
        Assert.Equal(bars, withSmoothing.DarkRuns);
        Assert.Equal(bars, without.DarkRuns);

        // The bars themselves are hard either way. Worth stating rather than assuming: a
        // page rendered for printing is already drawn without path anti-aliasing, so the
        // switch is not what makes a bar edge sharp -- rendering at the size it prints at is.
        Assert.Equal(0, withSmoothing.SoftPixels);
        Assert.Equal(0, without.SoftPixels);

        if (engine != RasterCatalogue.Pdfium)
        {
            // The in-box Windows engine takes no smoothing flag, so there is nothing more to
            // assert about it. The sheet still shows both, which is the comparison.
            return;
        }

        // Where the switch does show is the text, and this is the whole measurement: with
        // smoothing off not one pixel of the page is left between ink and stock, so the
        // printer has nothing to halftone and a thermal head nothing to guess at.
        var pageWithSmoothing = BarcodeScan.SoftPixels(smoothed.Pixels, smoothed.Width, smoothed.Height, smoothed.BytesPerPixel);
        var pageWithout = BarcodeScan.SoftPixels(sharp.Pixels, sharp.Width, sharp.Height, sharp.BytesPerPixel);

        Assert.Equal(0, pageWithout);
        Assert.True(pageWithSmoothing > 0, "Smoothing on should leave grey pixels somewhere on the page, and this one has none.");
    }

    // The line through the middle of the barcode. The symbol is placed in points from the
    // bottom of the page, so the row is measured from the top and scaled by how tall the page
    // rendered, which is not always the resolution asked for. A page composed onto a larger
    // media is the same page shifted down, so the shift is added and not scaled.
    private static ScanLine ScanBarcode(PwgRasterReader.RasterPage scanned, int pageHeight, int shiftDown)
    {
        const double FromTopPoints = RasterDocuments.HeightPoints
            - RasterDocuments.BarcodeBottomPoints
            - (Barcode.HeightPoints / 2);
        var row = shiftDown + (int)(pageHeight * FromTopPoints / RasterDocuments.HeightPoints);
        return BarcodeScan.Read(scanned.Pixels, scanned.Width, scanned.BytesPerPixel, row);
    }

    private static bool IsWhite(byte[] pixel) => Array.TrueForAll(pixel, value => value > 240);

    private static PrintConversionContext Context() =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, Dpi, null, "scenario");

    private static PrintConversionContext Selecting(int first, int second) =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, Dpi, [new PageRange(first, first), new PageRange(second, second)], "scenario");

    private static async Task<IReadOnlyList<PwgRasterReader.RasterPage>> RenderAsync(
        IPrintPayloadConverter converter,
        byte[] pdf,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var documents = await converter.ConvertAsync(pdf, context, cancellationToken).ConfigureAwait(false);

        // One PWG Raster stream carries every page, so a four-page document is one document.
        return PwgRasterReader.Read(Assert.Single(documents));
    }

    private static void AssertColour(IReadOnlyList<PwgRasterReader.RasterPage> pages)
    {
        Assert.Equal(RasterDocuments.PageCount, pages.Count);

        foreach (var page in pages)
        {
            Assert.Equal(24, page.BitsPerPixel);
            Assert.Equal(Dpi, page.ResolutionDpi);
            Assert.Equal(RasterDocuments.PageCount, page.TotalPageCount);
            Assert.Equal(pages[0].Width, page.Width);
            Assert.Equal(pages[0].Height, page.Height);

            // A4 is taller than it is wide, and the engines differ only in how many pixels
            // that becomes.
            Assert.True(page.Height > page.Width, $"{page.Width} by {page.Height} is not portrait.");
        }

        for (var index = 0; index < pages.Count; index++)
        {
            // The band is what says the page rendered at all, that it is the page it should
            // be, and that blue and red did not change places on the way out of the engine.
            (var red, var green, var blue) = RasterDocuments.BandColors[index];
            Assert.Equal<byte[]>([red, green, blue], BandPixel(pages[index]));

            // The corner block is in the bottom-left of the sheet and nowhere else, which is
            // what makes a flipped back side visible below.
            Assert.True(IsDark(CornerPixel(pages[index], top: false)), $"Page {index + 1} has no corner block at the bottom.");
            Assert.False(IsDark(CornerPixel(pages[index], top: true)), $"Page {index + 1} has a corner block at the top.");
        }
    }

    private static void AssertGrayscale(
        IReadOnlyList<PwgRasterReader.RasterPage> pages,
        IReadOnlyList<PwgRasterReader.RasterPage> colour)
    {
        Assert.Equal(RasterDocuments.PageCount, pages.Count);

        for (var index = 0; index < pages.Count; index++)
        {
            // One octet a pixel rather than three, and the same page at the same size: only
            // the colour space changed.
            Assert.Equal(8, pages[index].BitsPerPixel);
            Assert.Equal(colour[index].Width, pages[index].Width);
            Assert.Equal(colour[index].Height, pages[index].Height);
            Assert.Equal(pages[index].Width * pages[index].Height, pages[index].Pixels.Length);

            // The band is a saturated colour, so its luma is neither white nor black.
            var band = BandPixel(pages[index])[0];
            Assert.InRange(band, 1, 254);
        }
    }

    private static void AssertSelected(
        IReadOnlyList<PwgRasterReader.RasterPage> pages,
        IReadOnlyList<PwgRasterReader.RasterPage> colour)
    {
        // Two pages, and the same two the whole-document run produced: the strongest check
        // available that the converter selected pages 2 and 4 and not two other ones.
        Assert.Equal(2, pages.Count);
        Assert.Equal(colour[1].Pixels, pages[0].Pixels);
        Assert.Equal(colour[3].Pixels, pages[1].Pixels);

        // The header counts the pages of this document and not of the one it came from, or
        // the printer expects two more pages than it will ever receive.
        Assert.Equal(2, pages[0].TotalPageCount);
    }

    private static void AssertDuplex(
        IReadOnlyList<PwgRasterReader.RasterPage> pages,
        IReadOnlyList<PwgRasterReader.RasterPage> colour)
    {
        Assert.Equal(RasterDocuments.PageCount, pages.Count);

        // The scenario names a sheet-back of "flipped" although the job set names only the
        // duplex mode. With the default sheet-back the transform is the identity, and a
        // scenario that cannot tell a transformed page from an untransformed one proves
        // nothing about the one thing it exists to check.
        for (var index = 0; index < pages.Count; index++)
        {
            var isBackSide = index % 2 == 1;
            Assert.Equal(1, pages[index].CrossFeedTransform);
            Assert.Equal(isBackSide ? -1 : 1, pages[index].FeedTransform);

            if (!isBackSide)
            {
                Assert.Equal(colour[index].Pixels, pages[index].Pixels);
                continue;
            }

            // A flipped back side is written upside down, because the header only declares
            // the orientation and the printer transforms nothing itself. So the corner block
            // that is at the bottom of every front side is at the top of every back side.
            Assert.Equal(MirrorVertically(colour[index]), pages[index].Pixels);
            Assert.True(IsDark(CornerPixel(pages[index], top: true)), $"Page {index + 1} was not flipped.");
        }
    }

    // A page range and a duplex mode together. Which sheet is a back side is decided by the
    // position in the converted document, so selecting pages 2 and 4 makes the original page
    // 4 a back side and the original page 2 a front one.
    private static void AssertSelectedDuplex(
        IReadOnlyList<PwgRasterReader.RasterPage> pages,
        IReadOnlyList<PwgRasterReader.RasterPage> whole,
        int firstIndex,
        int secondIndex,
        int crossFeed,
        int feed)
    {
        Assert.Equal(2, pages.Count);
        Assert.Equal(2, pages[0].TotalPageCount);

        // The front is the selected page as it was rendered, transform or no transform.
        Assert.Equal(1, pages[0].CrossFeedTransform);
        Assert.Equal(1, pages[0].FeedTransform);
        Assert.Equal(whole[firstIndex].Pixels, pages[0].Pixels);

        // The back carries the coordinate system the printer asked for, and the pixels to
        // match it: long edge flips top to bottom, short edge flips left to right.
        Assert.Equal(crossFeed, pages[1].CrossFeedTransform);
        Assert.Equal(feed, pages[1].FeedTransform);
        Assert.Equal(
            feed == -1 ? MirrorVertically(whole[secondIndex]) : MirrorHorizontally(whole[secondIndex]),
            pages[1].Pixels);
    }

    private static byte[] MirrorHorizontally(PwgRasterReader.RasterPage page)
    {
        var mirrored = new byte[page.Pixels.Length];
        var stride = page.Width * page.BytesPerPixel;
        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                // A colour pixel is three octets that move together, so the copy is per
                // pixel and not per octet.
                Array.Copy(
                    page.Pixels,
                    (y * stride) + ((page.Width - 1 - x) * page.BytesPerPixel),
                    mirrored,
                    (y * stride) + (x * page.BytesPerPixel),
                    page.BytesPerPixel);
            }
        }

        return mirrored;
    }

    private static byte[] MirrorVertically(PwgRasterReader.RasterPage page)
    {
        var stride = page.Width * page.BytesPerPixel;
        var mirrored = new byte[page.Pixels.Length];
        for (var y = 0; y < page.Height; y++)
        {
            Array.Copy(page.Pixels, (page.Height - 1 - y) * stride, mirrored, y * stride, stride);
        }

        return mirrored;
    }

    private static byte[] BandPixel(PwgRasterReader.RasterPage page) =>
        PixelAt(page, page.Width / 2, (int)(page.Height * BandSampleY));

    private static byte[] CornerPixel(PwgRasterReader.RasterPage page, bool top)
    {
        var offset = (int)(page.Height * CornerSample);
        return PixelAt(page, (int)(page.Width * CornerSample), top ? offset : page.Height - 1 - offset);
    }

    private static byte[] PixelAt(PwgRasterReader.RasterPage page, int x, int y)
    {
        var start = (y * page.Width * page.BytesPerPixel) + (x * page.BytesPerPixel);
        return page.Pixels[start..(start + page.BytesPerPixel)];
    }

    private static bool IsDark(byte[] pixel) => Array.TrueForAll(pixel, value => value < 64);
}
