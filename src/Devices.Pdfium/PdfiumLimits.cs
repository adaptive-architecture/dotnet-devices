namespace AdaptArch.Devices.Pdfium;

// What PDFium renders well, and how a page is sized before it reaches it.
//
// Separate from PdfiumRenderer on purpose: this is arithmetic that describes the engine,
// not a call into it, so it is checked without a PDF and without the native library. The
// renderer keeps only the engine calls.
internal static class PdfiumLimits
{
    // FPDF_GetPageSizeByIndexF answers in PostScript points, and a point is 1/72 inch.
    // Windows.Data.Pdf counts device-independent pixels at 1/96 instead, which is the one
    // difference between this file and WindowsPdfLimits.
    internal const double PointsPerInch = 72.0;

    // Below the first a page turns to mush, above the second an A4 page needs more memory
    // than an inkjet job should hold. The band is stated here, and not on the spooler path,
    // because it describes this engine only.
    internal const int MinDpi = 150;
    internal const int MaxDpi = 600;

    // No rendered side exceeds this, whatever the units turn out to be, so a poster-size
    // page cannot exhaust the memory of an inkjet job. The number is the long side of Legal
    // at MaxDpi, so every common office medium renders at the full resolution asked for. A
    // page above it renders smaller than that resolution, and PrintScaling.None then prints
    // it smaller than its own size, because the spooler sizes a converted page from the
    // resolution it asked the converter for.
    internal const int MaxRenderPixels = 8400;

    internal static int ClampDpi(int dpi) => Math.Clamp(dpi, MinDpi, MaxDpi);

    // The cap belongs to the longer side, and both sides take the same factor. Capping each
    // side on its own would make an A4 page at 600 dots per inch square, because both sides
    // are then above the cap and both stop at it.
    internal static (int Width, int Height) RenderPixels(double widthPoints, double heightPoints, int dpi)
    {
        var width = widthPoints * dpi / PointsPerInch;
        var height = heightPoints * dpi / PointsPerInch;
        var longest = Math.Max(width, height);
        var scale = longest > MaxRenderPixels ? MaxRenderPixels / longest : 1.0;
        return (ToPixelCount(width * scale), ToPixelCount(height * scale));
    }

    // A page always renders at least one pixel a side, and never more than the cap: the
    // round up above can pass it by one when the scale lands on it exactly.
    private static int ToPixelCount(double value) =>
        Math.Max(1, Math.Min((int)Math.Ceiling(value), MaxRenderPixels));
}
