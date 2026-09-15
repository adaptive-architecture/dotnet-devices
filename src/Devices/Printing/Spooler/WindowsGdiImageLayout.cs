namespace AdaptArch.Devices.Printing.Spooler;

// Sizes and positions one image on one GDI printer page, in device pixels.
// P/Invoke-free on purpose, so the fit math is testable on any platform.
internal static class WindowsGdiImageLayout
{
    // GDI+ world-transform rotation is clockwise. IPP orientation-requested is
    // counter-clockwise from portrait, so landscape is -90 here.
    internal static float RotationDegrees(PrintOrientation? orientation)
    {
        if (orientation == PrintOrientation.Landscape)
        {
            return -90f;
        }

        if (orientation == PrintOrientation.ReverseLandscape)
        {
            return 90f;
        }

        if (orientation == PrintOrientation.ReversePortrait)
        {
            return 180f;
        }

        return 0f;
    }

    // A 90-degree turn swaps the axes the fit is judged on.
    internal static bool IsSideways(PrintOrientation? orientation) =>
        orientation is PrintOrientation.Landscape or PrintOrientation.ReverseLandscape;

    // How many device pixels one side of the image covers when it prints at its own
    // size: its pixels read at the resolution the source declares, and written at the
    // resolution of the device. Fit, Fill and AutoFit take this size too, because the
    // shape it gives is the shape the source really has, which a pixel count alone
    // does not carry.
    //
    // CUPS does the same on the Linux side, but it assumes 200 dots an inch for a file
    // that declares nothing, while GDI+ answers 96 for such a file and cannot tell it
    // apart from one that declares 96. So the same bare file prints smaller here.
    internal static int NaturalPixels(int imagePixels, double sourceDpi, int deviceDpi) =>
        imagePixels > 0 && sourceDpi > 0 && deviceDpi > 0
            ? Math.Max(1, (int)Math.Round(imagePixels * deviceDpi / sourceDpi))
            : imagePixels;

    // The destination rectangle of the image, centered, in the frame the caller draws
    // in: that is, after the rotation, where the image keeps its own axes. The page is
    // the printable area from GetDeviceCaps(HORZRES, VERTRES), so no margin math
    // lives here. Returns empty when either side has no size to fit.
    internal static ImageRectangle Compute(
        int imageWidth,
        int imageHeight,
        int pageWidth,
        int pageHeight,
        PrintOrientation? orientation,
        PrintScaling? scaling)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || pageWidth <= 0 || pageHeight <= 0)
        {
            return ImageRectangle.Empty;
        }

        var sideways = IsSideways(orientation);
        var fittedWidth = sideways ? imageHeight : imageWidth;
        var fittedHeight = sideways ? imageWidth : imageHeight;

        var mode = scaling ?? PrintScaling.Fit;
        if (mode == PrintScaling.Auto)
        {
            mode = PrintScaling.Fit;
        }

        double width = fittedWidth;
        double height = fittedHeight;
        if (mode == PrintScaling.None)
        {
            // Natural size, centered. A larger image is clipped by the driver.
            width = fittedWidth;
            height = fittedHeight;
        }
        else if (mode == PrintScaling.AutoFit)
        {
            if (fittedWidth <= pageWidth && fittedHeight <= pageHeight)
            {
                width = fittedWidth;
                height = fittedHeight;
            }
            else
            {
                var fit = Math.Min((double)pageWidth / fittedWidth, (double)pageHeight / fittedHeight);
                width = fittedWidth * fit;
                height = fittedHeight * fit;
            }
        }
        else if (mode == PrintScaling.Fit)
        {
            var fit = Math.Min((double)pageWidth / fittedWidth, (double)pageHeight / fittedHeight);
            width = fittedWidth * fit;
            height = fittedHeight * fit;
        }
        else if (mode == PrintScaling.Fill)
        {
            var fill = Math.Max((double)pageWidth / fittedWidth, (double)pageHeight / fittedHeight);
            width = fittedWidth * fill;
            height = fittedHeight * fill;
        }

        // The fit above judged the footprint the turned image leaves on the page. The
        // draw happens after the rotation, where the image keeps its own axes, so the
        // sides go back. Drawing the page-side footprint instead would squeeze the
        // image into an inverted aspect ratio.
        var drawWidth = sideways ? height : width;
        var drawHeight = sideways ? width : height;

        // The page center is the fixed point of the rotation, so a rectangle centered
        // there stays centered on the page.
        var x = (pageWidth - drawWidth) / 2.0;
        var y = (pageHeight - drawHeight) / 2.0;
        return new ImageRectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(drawWidth), (int)Math.Round(drawHeight));
    }
}

// A device-pixel rectangle. X and Y can be negative when the image is larger
// than the page, which is how clipping is expressed to GDI+.
internal readonly record struct ImageRectangle(int X, int Y, int Width, int Height)
{
    internal static readonly ImageRectangle Empty = new(0, 0, 0, 0);

    internal bool IsEmpty => Width <= 0 || Height <= 0;
}
