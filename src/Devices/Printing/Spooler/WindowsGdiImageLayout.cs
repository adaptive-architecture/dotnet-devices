namespace AdaptArch.Devices.Printing.Spooler;

// What GDI+ itself needs to know about an orientation. The sizing and the positioning live
// in ImagePlacement, which is public and platform-free, so the spooler path and a converter
// place a page with one arithmetic instead of two that drift.
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

    internal static bool IsSideways(PrintOrientation? orientation) => ImagePlacement.IsSideways(orientation);

    internal static int NaturalPixels(int imagePixels, double sourceDpi, int deviceDpi) =>
        ImagePlacement.NaturalPixels(imagePixels, sourceDpi, deviceDpi);

    internal static ImageRectangle Compute(
        int imageWidth,
        int imageHeight,
        int pageWidth,
        int pageHeight,
        PrintOrientation? orientation,
        PrintScaling? scaling,
        bool borderless = false,
        PrintPlacement? placement = null,
        int dpiX = 0,
        int dpiY = 0) =>
        ImagePlacement.Compute(
            imageWidth,
            imageHeight,
            pageWidth,
            pageHeight,
            orientation,
            scaling,
            borderless,
            placement?.Anchor ?? PrintAnchor.Center,
            Pixels(placement?.OffsetX, dpiX),
            Pixels(placement?.OffsetY, dpiY));

    // A placement with no resolution to measure against moves nothing: the caller has the
    // device context, and a page that shifted by a guess is worse than one that did not.
    private static int Pixels(PrintLength? offset, int dpi) =>
        offset is PrintLength length && dpi > 0 ? length.ToPixels(dpi) : 0;
}
