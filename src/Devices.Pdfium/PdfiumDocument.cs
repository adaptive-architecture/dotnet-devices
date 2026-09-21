namespace AdaptArch.Devices.Pdfium;

/// <summary>
/// Renders a PDF to pixels with PDFium, the engine behind Chrome and Edge.
/// </summary>
/// <remarks>
/// This is the entry point for an application that places a page itself — a label offset, a
/// media size taken from the document, a composition this library does not model — and it is
/// what the built-in converter renders with, so the surface is proved by a caller inside the
/// library rather than only by the one outside it.
/// <para>
/// <b>Render through this and not through PDFium directly.</b> The engine is not thread-safe
/// and has no lock of its own; this type holds one gate around every call into it, including
/// the one-time initialization. The gate works because it is the only door. A second binding
/// to the same native library in the same process — and it is the same process, because the
/// built-in converter is still registered — has no way to see this one, and the failure mode
/// is the process rather than the job.
/// </para>
/// </remarks>
public static class PdfiumDocument
{
    /// <summary>The lowest resolution the engine renders well. Below it a page turns to mush.</summary>
    public const int MinDpi = PdfiumLimits.MinDpi;

    /// <summary>The highest resolution this library asks for. Above it a page costs more memory than a job should hold.</summary>
    public const int MaxDpi = PdfiumLimits.MaxDpi;

    /// <summary>
    /// Renders the selected pages, in document order.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <param name="options">The resolution, the pages, the colour space and the smoothing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One rendered page for each page selected.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the document or the options are <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the document cannot be read, has no pages, or the ranges select none.</exception>
    /// <remarks>
    /// A page longer than the library renders at the resolution asked for is rendered smaller
    /// than that resolution, so that a poster-size page cannot exhaust the memory of one job.
    /// The page box each answer carries is the size the document asked for, whatever the
    /// pixels ended up being.
    /// </remarks>
    public static Task<IReadOnlyList<PdfPage>> RenderAsync(
        byte[] pdf,
        PdfRenderOptions options,
        CancellationToken cancellationToken = default) =>
        PdfiumRenderer.RenderAsync(pdf, options, cancellationToken);
}
