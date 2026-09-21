namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// What a PDF engine renders well, and how a page is sized before it reaches one.
/// </summary>
/// <remarks>
/// Arithmetic that describes an engine rather than a call into one, so it is checked without
/// a PDF and without a native library. It lives here, and not in a rasterizer package,
/// because every engine has to answer the same page the same size: the engines differ only
/// in the unit their page box is measured in, which each one passes to
/// <see cref="RenderPixels"/>.
/// </remarks>
public static class PdfRenderLimits
{
    /// <summary>
    /// The PostScript points an inch. A PDF page box is authored in them, and PDFium's
    /// <c>FPDF_GetPageSizeByIndexF</c> answers in them.
    /// </summary>
    public const double PointsPerInch = 72.0;

    /// <summary>
    /// The device-independent pixels an inch. The in-box Windows engine counts a page in
    /// them rather than in points, and that is the only way the two engines differ here.
    /// </summary>
    public const double DipsPerInch = 96.0;

    /// <summary>The lowest resolution an engine renders well. Below it a page turns to mush.</summary>
    public const int MinDpi = 150;

    /// <summary>The highest resolution this library asks for. Above it an A4 page needs more memory than an inkjet job should hold.</summary>
    public const int MaxDpi = 600;

    /// <summary>
    /// The longest side, in pixels, a rendered page may take.
    /// </summary>
    /// <remarks>
    /// It is A3 at <see cref="MaxDpi"/>, so every common office medium renders at the full
    /// resolution asked for. A page above it renders smaller than that resolution, and
    /// <see cref="PrintScaling.None"/> then prints it smaller than its own size, because a
    /// channel sizes a converted page from the resolution it asked the converter for.
    /// </remarks>
    public const int MaxRenderPixels = 8400;

    /// <summary>
    /// Brings a resolution inside the band an engine renders well.
    /// </summary>
    /// <param name="dpi">The resolution asked for, in dots per inch.</param>
    /// <returns>The resolution to render at.</returns>
    public static int ClampDpi(int dpi) => Math.Clamp(dpi, MinDpi, MaxDpi);

    /// <summary>
    /// Sizes the bitmap a page renders into.
    /// </summary>
    /// <param name="width">The width of the page box.</param>
    /// <param name="height">The height of the page box.</param>
    /// <param name="dpi">The resolution to render at, already clamped.</param>
    /// <param name="unitsPerInch">The unit the page box is measured in: <see cref="PointsPerInch"/> or <see cref="DipsPerInch"/>.</param>
    /// <returns>The pixels a side, never below one and never above <see cref="MaxRenderPixels"/>.</returns>
    /// <remarks>
    /// The cap belongs to the longer side, and both sides take the same factor. Capping each
    /// side on its own would make an A4 page at 600 dots per inch square, because both sides
    /// are then above the cap and both stop at it.
    /// </remarks>
    public static (int Width, int Height) RenderPixels(double width, double height, int dpi, double unitsPerInch)
    {
        var pixelWidth = width * dpi / unitsPerInch;
        var pixelHeight = height * dpi / unitsPerInch;
        var longest = Math.Max(pixelWidth, pixelHeight);
        var scale = longest > MaxRenderPixels ? MaxRenderPixels / longest : 1.0;
        return (ToPixelCount(pixelWidth * scale), ToPixelCount(pixelHeight * scale));
    }

    // A page always renders at least one pixel a side, and never more than the cap: the
    // round up above can pass it by one when the scale lands on it exactly.
    private static int ToPixelCount(double value) =>
        Math.Max(1, Math.Min((int)Math.Ceiling(value), MaxRenderPixels));
}
