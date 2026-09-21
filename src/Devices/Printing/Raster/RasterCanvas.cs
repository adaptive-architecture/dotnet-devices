namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Composes a rendered page onto a canvas the size of the media.
/// </summary>
/// <remarks>
/// This is the step that puts a placement into the pixels, for a channel that applies none
/// itself: an IPP printer reads the raster it is given and has no attribute for a label
/// offset, so the offset has to be in the bytes before they are sent.
/// <para>
/// It lives beside <see cref="PwgRasterWriter"/> and is public for the same reason: an
/// application with its own rasterizer places a page with the same arithmetic the library
/// uses, instead of a second one that drifts. No dependency and no platform call.
/// </para>
/// </remarks>
public static class RasterCanvas
{
    /// <summary>
    /// Draws one page into a white canvas.
    /// </summary>
    /// <param name="pixels">The page, packed with no padding between its lines.</param>
    /// <param name="width">The width of the page in pixels.</param>
    /// <param name="height">The height of the page in pixels.</param>
    /// <param name="bytesPerPixel">One for grayscale, three for red, green, blue.</param>
    /// <param name="target">The canvas, where on it the page goes, and how its pixels are read.</param>
    /// <returns>The canvas, packed with no padding between its lines.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a size is not positive, or the pixels are too few for the page.</exception>
    /// <remarks>
    /// A destination that leaves the canvas is clipped and not refused: a page larger than
    /// its media, or one an offset pushed off its stock, prints what fits. That is what a
    /// person calibrating an offset needs to see, and it is what every printer does with a
    /// page too big for its tray.
    /// </remarks>
    public static byte[] Compose(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        RasterTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var canvasWidth = target.Width;
        var canvasHeight = target.Height;
        var destination = target.Destination;
        var resampling = target.Resampling;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        if (bytesPerPixel != 1 && bytesPerPixel != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), bytesPerPixel, "A page is one octet a pixel or three.");
        }

        var sourceStride = width * bytesPerPixel;
        if (pixels.Length < sourceStride * height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixels),
                pixels.Length,
                $"A page of {width} by {height} pixels needs {sourceStride * height} octets.");
        }

        var canvasStride = canvasWidth * bytesPerPixel;
        var canvas = new byte[canvasStride * canvasHeight];

        // White, so the part of the media the page does not cover prints as the stock it is.
        // Both colour spaces write white as every octet set.
        canvas.AsSpan().Fill(0xFF);

        if (destination.IsEmpty)
        {
            return canvas;
        }

        // Only the part of the destination that lands on the canvas is drawn; the rest is
        // the clipping. The source coordinate is still measured from the whole destination,
        // so a clipped page keeps the scale it would have had.
        var fromX = Math.Max(0, destination.X);
        var fromY = Math.Max(0, destination.Y);
        var toX = Math.Min(canvasWidth, destination.X + destination.Width);
        var toY = Math.Min(canvasHeight, destination.Y + destination.Height);

        for (var y = fromY; y < toY; y++)
        {
            var sourceY = (y - destination.Y + 0.5) * height / destination.Height;
            for (var x = fromX; x < toX; x++)
            {
                var sourceX = (x - destination.X + 0.5) * width / destination.Width;
                var at = (y * canvasStride) + (x * bytesPerPixel);
                if (resampling == RasterResampling.Bilinear)
                {
                    Bilinear(pixels, width, height, bytesPerPixel, sourceX, sourceY, canvas.AsSpan(at, bytesPerPixel));
                }
                else
                {
                    Nearest(pixels, width, height, bytesPerPixel, sourceX, sourceY, canvas.AsSpan(at, bytesPerPixel));
                }
            }
        }

        return canvas;
    }

    private static void Nearest(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        double sourceX,
        double sourceY,
        Span<byte> target)
    {
        var x = Clamp((int)sourceX, width);
        var y = Clamp((int)sourceY, height);
        var source = (y * width * bytesPerPixel) + (x * bytesPerPixel);
        pixels.Slice(source, bytesPerPixel).CopyTo(target);
    }

    private static void Bilinear(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int bytesPerPixel,
        double sourceX,
        double sourceY,
        Span<byte> target)
    {
        // The sample sits at the centre of its destination pixel, so the two neighbours are
        // half a pixel either side of it.
        var left = (int)Math.Floor(sourceX - 0.5);
        var top = (int)Math.Floor(sourceY - 0.5);
        var alongX = sourceX - 0.5 - left;
        var alongY = sourceY - 0.5 - top;

        var x0 = Clamp(left, width);
        var x1 = Clamp(left + 1, width);
        var y0 = Clamp(top, height);
        var y1 = Clamp(top + 1, height);

        var stride = width * bytesPerPixel;
        for (var channel = 0; channel < bytesPerPixel; channel++)
        {
            var topLeft = pixels[(y0 * stride) + (x0 * bytesPerPixel) + channel];
            var topRight = pixels[(y0 * stride) + (x1 * bytesPerPixel) + channel];
            var bottomLeft = pixels[(y1 * stride) + (x0 * bytesPerPixel) + channel];
            var bottomRight = pixels[(y1 * stride) + (x1 * bytesPerPixel) + channel];

            var upper = topLeft + ((topRight - topLeft) * alongX);
            var lower = bottomLeft + ((bottomRight - bottomLeft) * alongX);
            var value = upper + ((lower - upper) * alongY);
            target[channel] = (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        }
    }

    private static int Clamp(int value, int size) => Math.Clamp(value, 0, size - 1);
}
