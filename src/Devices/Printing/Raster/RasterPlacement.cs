namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Puts one rendered page where the job asked for it on the media.
/// </summary>
/// <remarks>
/// The one place the fit, the anchor, the offset and the resampling meet, so that every
/// rasterizer package places a page the same way and a second engine cannot drift from the
/// first. A converter renders, calls this, and encodes.
/// </remarks>
public static class RasterPlacement
{
    /// <summary>
    /// Composes a page onto its media, when the channel said what that media is.
    /// </summary>
    /// <param name="pixels">The rendered page, packed with no padding between the lines.</param>
    /// <param name="width">The width of the page in pixels.</param>
    /// <param name="height">The height of the page in pixels.</param>
    /// <param name="bytesPerPixel">One for grayscale, three for red, green, blue.</param>
    /// <param name="context">What the channel is asking the converter for.</param>
    /// <returns>
    /// The composed page, or <c>null</c> when there is nothing to compose onto and the page
    /// stands as it was rendered.
    /// </returns>
    /// <remarks>
    /// It answers <c>null</c> in the two cases where the page is already the right thing: a
    /// channel that named no media, such as the Windows spooler, which converts before it
    /// knows the sheet and places the page at the draw step instead; and a job whose media is
    /// the document, where the page is its own media and no fit, offset or resampling
    /// applies.
    /// </remarks>
    public static ComposedPage? Place(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        PrintConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.MediaSizeSource == MediaSizeSource.Document)
        {
            return null;
        }

        if (context.MediaWidthPixels is not int canvasWidth || context.MediaHeightPixels is not int canvasHeight)
        {
            return null;
        }

        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return null;
        }

        var placement = context.Placement;

        // The page is fitted into, and anchored against, the part of the media the job asked
        // for: the whole sheet, or the part of it the printer can mark. The canvas stays the
        // sheet either way, because that is what the printer expects to receive.
        var area = context.FitArea ?? new ImageRectangle(0, 0, canvasWidth, canvasHeight);
        if (area.IsEmpty)
        {
            area = new ImageRectangle(0, 0, canvasWidth, canvasHeight);
        }

        // Nothing of ours turns these pixels: the printer applies the orientation itself, and
        // turning them here would apply it twice.
        var placed = ImagePlacement.Compute(
            width,
            height,
            area.Width,
            area.Height,
            null,
            context.Scaling,
            new ImagePlacementOptions
            {
                // A fit to the whole sheet is a fit with no margin to clear, which is what
                // borderless means to the Auto mode.
                Borderless = area.Width >= canvasWidth && area.Height >= canvasHeight,
                Anchor = placement?.Anchor ?? PrintAnchor.Center,
                OffsetX = placement?.OffsetX.ToPixels(context.Dpi) ?? 0,
                OffsetY = placement?.OffsetY.ToPixels(context.Dpi) ?? 0,
            });

        var destination = new ImageRectangle(placed.X + area.X, placed.Y + area.Y, placed.Width, placed.Height);

        // A page that already covers its media exactly, with nothing to move it, is the page
        // it was rendered as. Composing it would copy every pixel to say the same thing.
        if (destination.X == 0
            && destination.Y == 0
            && destination.Width == width
            && destination.Height == height
            && canvasWidth == width
            && canvasHeight == height)
        {
            return null;
        }

        var composed = RasterCanvas.Compose(
            pixels,
            width,
            height,
            bytesPerPixel,
            new RasterTarget
            {
                Width = canvasWidth,
                Height = canvasHeight,
                Destination = destination,
                Resampling = ResamplingFor(context.Smoothing),
            });

        return new ComposedPage(composed, canvasWidth, canvasHeight);
    }

    /// <summary>
    /// Tells whether <see cref="Place"/> answers the final geometry for this context.
    /// </summary>
    /// <param name="context">What the channel is asking the converter for.</param>
    /// <returns><c>true</c> when the pages a converter returns are the media, and the printer must fit nothing.</returns>
    /// <remarks>
    /// True in three cases, which are one case: the job takes its media from the document, so
    /// the page is its own media; the channel named the media and the page was composed onto
    /// it; or the page already covered that media exactly and needed no composing. In each the
    /// pixels are what should reach the paper.
    /// </remarks>
    public static bool PlacesOnMedia(PrintConversionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.MediaSizeSource == MediaSizeSource.Document
            || (context.MediaWidthPixels > 0 && context.MediaHeightPixels > 0);
    }

    /// <summary>
    /// The resampling a job's smoothing switch asks for.
    /// </summary>
    /// <param name="smoothing">The switch, or <c>null</c> for the default.</param>
    /// <returns>Nearest-neighbour when smoothing is off, bilinear otherwise.</returns>
    public static RasterResampling ResamplingFor(bool? smoothing) =>
        smoothing == false ? RasterResampling.NearestNeighbor : RasterResampling.Bilinear;
}

/// <summary>
/// A page after it has been composed onto its media.
/// </summary>
/// <param name="Pixels">The pixels, packed with no padding between the lines.</param>
/// <param name="Width">The width in pixels, which is the width of the media.</param>
/// <param name="Height">The height in pixels, which is the height of the media.</param>
public sealed record ComposedPage(byte[] Pixels, int Width, int Height);
