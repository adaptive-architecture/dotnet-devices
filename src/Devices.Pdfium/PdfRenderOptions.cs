using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Pdfium;

/// <summary>
/// What <see cref="PdfiumDocument"/> is asked to render.
/// </summary>
public sealed record PdfRenderOptions
{
    /// <summary>
    /// Gets the resolution to render at, in dots per inch. It is clamped to what the engine
    /// renders well, which is <see cref="PdfRenderLimits.MinDpi"/> to
    /// <see cref="PdfRenderLimits.MaxDpi"/>.
    /// </summary>
    public int Dpi { get; init; } = PrintConversionContext.DefaultDpi;

    /// <summary>
    /// Gets the 1-based pages to render, in document order, or <c>null</c> for the whole
    /// document.
    /// </summary>
    public IReadOnlyList<PageRange>? PageRanges { get; init; }

    /// <summary>
    /// Gets the colour space of the pixels: three octets a pixel, or one.
    /// </summary>
    public PwgRasterColorSpace ColorSpace { get; init; } = PwgRasterColorSpace.Srgb8;

    /// <summary>
    /// Gets the password that opens the document, or <c>null</c> for one that needs none.
    /// </summary>
    /// <remarks>
    /// It opens the document and goes no further: no printer protocol carries it, nothing
    /// logs it, and it is not part of any job the library sends.
    /// </remarks>
    public string? Password { get; init; }

    /// <summary>
    /// Gets whether the engine smooths the text, the images and the paths it draws.
    /// Defaults to <c>true</c>, which is what PDFium does when nothing says otherwise.
    /// </summary>
    /// <remarks>
    /// Off is what a barcode on a thermal head needs: an anti-aliased bar edge arrives as a
    /// grey ramp, the printer halftones the ramp, and a scanner reads the result as nothing.
    /// </remarks>
    public bool Smoothing { get; init; } = true;
}
