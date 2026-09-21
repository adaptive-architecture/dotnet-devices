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

        // Nothing of ours turns these pixels: the printer applies the orientation itself, and
        // turning them here would apply it twice.
        var destination = ImagePlacement.Compute(
            width,
            height,
            canvasWidth,
            canvasHeight,
            null,
            context.Scaling,
            false,
            placement?.Anchor ?? PrintAnchor.Center,
            placement?.OffsetX.ToPixels(context.Dpi) ?? 0,
            placement?.OffsetY.ToPixels(context.Dpi) ?? 0);

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
            canvasWidth,
            canvasHeight,
            destination,
            ResamplingFor(context.Smoothing));

        return new ComposedPage(composed, canvasWidth, canvasHeight);
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
