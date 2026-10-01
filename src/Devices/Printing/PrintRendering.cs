namespace AdaptArch.Devices.Printing;

/// <summary>
/// How a document reaches the printer driver: as the drawing it describes, or as one bitmap a
/// page.
/// </summary>
/// <remarks>
/// Only the Windows spooler can take a document as drawing, and only from a converter that
/// draws onto a device context, which <c>AdaptArch.Devices.Pdfium</c> does. Every other channel
/// and engine renders a bitmap, so an explicit <see cref="Vector"/> there is reported in
/// <see cref="PrintJobInfo.DroppedOptionDetails"/>.
/// </remarks>
public enum PrintRendering
{
    /// <summary>
    /// The engine draws text, lines and shapes into the printer device context, and the
    /// driver renders them at its own resolution. Spool jobs are smaller and text stays sharp.
    /// </summary>
    /// <remarks>
    /// The engine still rasterizes what GDI cannot draw, such as transparency, so a page may
    /// carry some bitmaps anyway. The driver then decides how the bars of a barcode are
    /// halftoned, so check a label printer before choosing it there.
    /// </remarks>
    Vector,

    /// <summary>
    /// The engine renders each page to a bitmap at <see cref="PrintOptions.ResolutionDpi"/>,
    /// and the bitmap is what the driver receives. This is the default.
    /// </summary>
    /// <remarks>
    /// The output then matches what the same engine sends an IPP printer, and the edge of a
    /// barcode is decided by our resampling rather than by the driver's halftoning.
    /// </remarks>
    Raster,
}
