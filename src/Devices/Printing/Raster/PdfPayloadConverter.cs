namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// A converter that reads PDF and writes whichever raster format the channel asks for.
/// </summary>
/// <remarks>
/// Everything about such a converter except the engine call: the formats it reads and
/// writes, the resolution it clamps to, the placement onto the media, and the encoding of
/// the answer. A rasterizer package derives from this and overrides
/// <see cref="RenderAsync"/> with the one thing that is its own, so that a second engine
/// cannot drift from the first.
/// <para>
/// It writes two formats, because the two channels that convert want different things: the
/// Windows spooler draws PNG pages through GDI, and an IPP printer reads PWG Raster and
/// never PNG. The target the context names decides which.
/// </para>
/// </remarks>
public abstract class PdfPayloadConverter : IPrintPayloadConverter
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool CanEmit(string targetContentType) =>
        String.Equals(targetContentType, PrinterContentTypes.Png, StringComparison.OrdinalIgnoreCase)
        || String.Equals(targetContentType, PrinterContentTypes.PwgRaster, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    /// <remarks>
    /// This converter composes onto the media when the channel names one, so the fit is in
    /// the pixels and the printer must not apply it a second time.
    /// </remarks>
    public bool PlacesOnMedia(PrintConversionContext context) => RasterPlacement.PlacesOnMedia(context);

    /// <summary>
    /// Renders the pages the context selects, in document order.
    /// </summary>
    /// <param name="pdf">The document.</param>
    /// <param name="context">What the channel is asking the converter for.</param>
    /// <param name="dpi">The resolution to render at, already clamped to <see cref="PdfRenderLimits"/>.</param>
    /// <param name="colorSpace">The colour space to render in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One rendered page for each page selected.</returns>
    /// <remarks>
    /// The resolution and the colour space are passed rather than read from the context,
    /// because the target decides both: a PNG page is always colour, and a raster page is
    /// whatever the printer asked for.
    /// </remarks>
    protected abstract Task<IReadOnlyList<RenderedPdfPage>> RenderAsync(
        byte[] pdf,
        PrintConversionContext context,
        int dpi,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return String.Equals(context.TargetContentType, PrinterContentTypes.PwgRaster, StringComparison.OrdinalIgnoreCase)
            ? ConvertToRasterAsync(data, context, cancellationToken)
            : ConvertToPngAsync(data, context, cancellationToken);
    }

    // One PNG a page, which is what the Windows spooler asks for and draws through GDI.
    // Always in colour: the spooler names no colour space, and a printer asked for grey
    // prints a colour page in grey, where the other way round loses the colour for good.
    private async Task<IReadOnlyList<byte[]>> ConvertToPngAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var dpi = PdfRenderLimits.ClampDpi(context.Dpi);
        var pages = await RenderAsync(data, context, dpi, PwgRasterColorSpace.Srgb8, cancellationToken).ConfigureAwait(false);

        List<byte[]> images = new(pages.Count);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var placed = Place(page, context);
            images.Add(PngWriter.Encode(placed.Pixels, placed.Width, placed.Height, PngColorType.Rgb8, dpi));
        }

        return images;
    }

    // One PWG Raster stream carries every page, so this answers with a single document and
    // not with one a page. Pages are written as they are rendered, because a document held
    // whole would cost more memory than the job it prints.
    private async Task<IReadOnlyList<byte[]>> ConvertToRasterAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var dpi = PdfRenderLimits.ClampDpi(context.Dpi);
        var colorSpace = PwgRaster.ColorSpaceFor(context.RasterType);
        var pages = await RenderAsync(data, context, dpi, colorSpace, cancellationToken).ConfigureAwait(false);

        await using MemoryStream document = new();
        PwgRasterWriter writer = new(document, new PwgRasterOptions
        {
            ResolutionDpi = dpi,
            ColorSpace = colorSpace,
            TotalPageCount = pages.Count,
            Duplex = context.Duplex,
            SheetBack = PwgRaster.SheetBackFor(context.SheetBack),
            MediaName = context.MediaName,
        });

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var placed = Place(page, context);
            writer.WritePage(placed.Pixels, placed.Width, placed.Height);
        }

        return [document.ToArray()];
    }

    // A channel that named its media gets a page the size of that media, with the fit, the
    // anchor and the offset already in the pixels. One that named none gets the page as it
    // was rendered, and places it itself.
    private static ComposedPage Place(RenderedPdfPage page, PrintConversionContext context) =>
        RasterPlacement.Place(page.Pixels, page.Width, page.Height, page.BytesPerPixel, context)
        ?? new ComposedPage(page.Pixels, page.Width, page.Height);
}
