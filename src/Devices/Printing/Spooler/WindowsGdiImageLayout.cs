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

    // The destination rectangle of the image on the page, centered. The page is
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

        var x = (pageWidth - width) / 2.0;
        var y = (pageHeight - height) / 2.0;
        return new ImageRectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(width), (int)Math.Round(height));
    }
}

// A device-pixel rectangle. X and Y can be negative when the image is larger
// than the page, which is how clipping is expressed to GDI+.
internal readonly record struct ImageRectangle(int X, int Y, int Width, int Height)
{
    internal static readonly ImageRectangle Empty = new(0, 0, 0, 0);

    internal bool IsEmpty => Width <= 0 || Height <= 0;
}
