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
        DeviceLayout? layout = null) =>
        ImagePlacement.Compute(
            imageWidth,
            imageHeight,
            pageWidth,
            pageHeight,
            orientation,
            scaling,
            new ImagePlacementOptions
            {
                Borderless = layout?.Borderless ?? false,
                Anchor = layout?.Placement?.Anchor ?? PrintAnchor.Center,
                OffsetX = Pixels(layout?.Placement?.OffsetX, layout?.DpiX ?? 0),
                OffsetY = Pixels(layout?.Placement?.OffsetY, layout?.DpiY ?? 0),
            });

    // The same tail as ImagePlacementOptions, in the units a device speaks: a placement
    // measured in real lengths, and the resolutions that turn them into its pixels.
    internal sealed record DeviceLayout
    {
        public bool Borderless { get; init; }

        public PrintPlacement? Placement { get; init; }

        public int DpiX { get; init; }

        public int DpiY { get; init; }
    }

    // A placement with no resolution to measure against moves nothing: the caller has the
    // device context, and a page that shifted by a guess is worse than one that did not.
    private static int Pixels(PrintLength? offset, int dpi) =>
        offset is PrintLength length && dpi > 0 ? length.ToPixels(dpi) : 0;
}
