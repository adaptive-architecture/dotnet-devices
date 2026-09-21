namespace AdaptArch.Devices.Printing;

/// <summary>
/// Sizes and positions one page on one sheet, in device pixels.
/// </summary>
/// <remarks>
/// Pure arithmetic, with no platform call in it, so the same fit runs on every operating
/// system and is checked without a printer. It is what the Windows spooler draws with and
/// what a converter composes a raster with, so the two paths cannot drift apart.
/// </remarks>
public static class ImagePlacement
{
    /// <summary>
    /// The number of device pixels one side of a page covers when it prints at its own size.
    /// </summary>
    /// <param name="imagePixels">The side, in the pixels the source holds.</param>
    /// <param name="sourceDpi">The resolution the source declares.</param>
    /// <param name="deviceDpi">The resolution of the device.</param>
    /// <returns>The side in device pixels, or the source side when either resolution is unknown.</returns>
    /// <remarks>
    /// Every mode takes this size, because the shape it gives is the shape the source really
    /// has, which a pixel count alone does not carry, and because <see cref="PrintScaling.Auto"/>
    /// and <see cref="PrintScaling.AutoFit"/> decide on whether that size fits the sheet.
    /// </remarks>
    public static int NaturalPixels(int imagePixels, double sourceDpi, int deviceDpi) =>
        imagePixels > 0 && sourceDpi > 0 && deviceDpi > 0
            ? Math.Max(1, (int)Math.Round(imagePixels * deviceDpi / sourceDpi))
            : imagePixels;

    /// <summary>
    /// Computes where a page is drawn on a sheet.
    /// </summary>
    /// <param name="imageWidth">The page width in device pixels, at its own size.</param>
    /// <param name="imageHeight">The page height in device pixels, at its own size.</param>
    /// <param name="pageWidth">The width of the area drawn on.</param>
    /// <param name="pageHeight">The height of the area drawn on.</param>
    /// <param name="orientation">The orientation the page is turned by, or <c>null</c> for none.</param>
    /// <param name="scaling">How the page is fitted, or <c>null</c> for the printer default.</param>
    /// <param name="borderless">Whether the area drawn on is the whole sheet rather than the printable part of it.</param>
    /// <param name="anchor">Where the fitted page sits before the offset moves it.</param>
    /// <param name="offsetX">How far the page moves right on the media, in device pixels.</param>
    /// <param name="offsetY">How far the page moves down the media, in device pixels.</param>
    /// <returns>The rectangle to draw in, or <see cref="ImageRectangle.Empty"/> when either side has no size to fit.</returns>
    /// <remarks>
    /// The rectangle is in the frame the caller draws in, which for a turned page is after
    /// the rotation, where the page keeps its own axes. The anchor and the offset are in the
    /// frame of the media, because that is where a person measures them.
    /// <para>
    /// A caller that composes a raster rather than drawing through a device passes <c>null</c>
    /// for <paramref name="orientation"/>: nothing of ours turns those pixels, and the
    /// printer applies the orientation itself.
    /// </para>
    /// </remarks>
    public static ImageRectangle Compute(
        int imageWidth,
        int imageHeight,
        int pageWidth,
        int pageHeight,
        PrintOrientation? orientation,
        PrintScaling? scaling,
        bool borderless = false,
        PrintAnchor anchor = PrintAnchor.Center,
        int offsetX = 0,
        int offsetY = 0)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || pageWidth <= 0 || pageHeight <= 0)
        {
            return ImageRectangle.Empty;
        }

        var sideways = IsSideways(orientation);
        var fittedWidth = sideways ? imageHeight : imageWidth;
        var fittedHeight = sideways ? imageWidth : imageHeight;
        var fits = fittedWidth <= pageWidth && fittedHeight <= pageHeight;

        // An unset option means the printer default, and PWG 5100.16 names Auto as that
        // default.
        var mode = Resolve(scaling ?? PrintScaling.Auto, fits, borderless);
        (var width, var height) = Size(mode, fits, fittedWidth, fittedHeight, pageWidth, pageHeight);

        // Where the footprint of the turned page sits on the media, which is what the anchor
        // and the offset are measured against.
        var left = AnchorStart(AcrossAnchor(anchor), pageWidth, width) + offsetX;
        var top = AnchorStart(DownAnchor(anchor), pageHeight, height) + offsetY;

        // The rotation turns the page about the centre of the sheet, so the draw frame is
        // reached by turning the displacement from that centre the other way. The sides go
        // back too: the fit judged the footprint the turned page leaves, and the draw happens
        // where the page keeps its own axes. Drawing the footprint instead would squeeze the
        // page into an inverted aspect ratio.
        var drawWidth = sideways ? height : width;
        var drawHeight = sideways ? width : height;
        var fromCenterX = left + (width / 2.0) - (pageWidth / 2.0);
        var fromCenterY = top + (height / 2.0) - (pageHeight / 2.0);
        (var drawFromCenterX, var drawFromCenterY) = ToDrawFrame(orientation, fromCenterX, fromCenterY);

        var x = (pageWidth / 2.0) + drawFromCenterX - (drawWidth / 2.0);
        var y = (pageHeight / 2.0) + drawFromCenterY - (drawHeight / 2.0);
        return new ImageRectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(drawWidth), (int)Math.Round(drawHeight));
    }

    // A quarter turn swaps the axes the fit is judged on.
    internal static bool IsSideways(PrintOrientation? orientation) =>
        orientation is PrintOrientation.Landscape or PrintOrientation.ReverseLandscape;

    // Turns a displacement measured on the media into the same displacement in the frame the
    // page is drawn in: the inverse of the rotation the caller applies. The angles are the
    // ones WindowsGdiImageLayout.RotationDegrees hands to the world transform, so a landscape
    // page is turned by a quarter and the inverse turns it back.
    private static (double X, double Y) ToDrawFrame(PrintOrientation? orientation, double x, double y)
    {
        if (orientation == PrintOrientation.Landscape)
        {
            return (-y, x);
        }

        if (orientation == PrintOrientation.ReverseLandscape)
        {
            return (y, -x);
        }

        if (orientation == PrintOrientation.ReversePortrait)
        {
            return (-x, -y);
        }

        return (x, y);
    }

    // Where one side of the fitted page starts: against the near edge, centred, or against
    // the far edge. A page larger than the media starts negative, which is the clipping.
    private static int AnchorStart(int side, int available, double covered)
    {
        if (side < 0)
        {
            return 0;
        }

        if (side > 0)
        {
            return (int)Math.Round(available - covered);
        }

        return (int)Math.Round((available - covered) / 2.0);
    }

    // -1 against the left edge, 0 centred, 1 against the right edge.
    private static int AcrossAnchor(PrintAnchor anchor)
    {
        if (anchor is PrintAnchor.TopLeft or PrintAnchor.CenterLeft or PrintAnchor.BottomLeft)
        {
            return -1;
        }

        if (anchor is PrintAnchor.TopRight or PrintAnchor.CenterRight or PrintAnchor.BottomRight)
        {
            return 1;
        }

        return 0;
    }

    // -1 against the top edge, 0 centred, 1 against the bottom edge.
    private static int DownAnchor(PrintAnchor anchor)
    {
        if (anchor is PrintAnchor.TopLeft or PrintAnchor.TopCenter or PrintAnchor.TopRight)
        {
            return -1;
        }

        if (anchor is PrintAnchor.BottomLeft or PrintAnchor.BottomCenter or PrintAnchor.BottomRight)
        {
            return 1;
        }

        return 0;
    }

    // PWG 5100.16: a document smaller than the media keeps its own size, and a larger one
    // fits inside the margins, or fills a sheet that has none. Every other mode says what it
    // wants and passes through.
    private static PrintScaling Resolve(PrintScaling mode, bool fits, bool borderless)
    {
        if (mode != PrintScaling.Auto)
        {
            return mode;
        }

        if (fits)
        {
            return PrintScaling.None;
        }

        return borderless ? PrintScaling.Fill : PrintScaling.Fit;
    }

    // What the page measures on the media, before the rotation puts its axes back. None is
    // the natural size, and a larger page is clipped; AutoFit is None for a page that fits
    // and Fit for one that does not.
    private static (double Width, double Height) Size(
        PrintScaling mode,
        bool fits,
        int fittedWidth,
        int fittedHeight,
        int pageWidth,
        int pageHeight)
    {
        if (mode == PrintScaling.Fill)
        {
            var fill = Math.Max((double)pageWidth / fittedWidth, (double)pageHeight / fittedHeight);
            return (fittedWidth * fill, fittedHeight * fill);
        }

        if (mode == PrintScaling.Fit || (mode == PrintScaling.AutoFit && !fits))
        {
            var fit = Math.Min((double)pageWidth / fittedWidth, (double)pageHeight / fittedHeight);
            return (fittedWidth * fit, fittedHeight * fit);
        }

        return (fittedWidth, fittedHeight);
    }
}
