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
/// It writes three formats, because the channels that convert want different things: the
/// Windows spooler draws PNG pages through GDI, an IPP printer reads PWG Raster and never
/// PNG, and an AirPrint printer or a macOS CUPS queue reads URF. The target the context
/// names decides which.
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
        || IsRaster(targetContentType);

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
    /// <param name="dpi">The resolution to render at, already clamped to <see cref="PdfRenderLimits"/>. The page is scaled to the context's resolution afterwards.</param>
    /// <param name="colorSpace">The colour space to render in.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One rendered page for each page selected, as each is rendered.</returns>
    /// <remarks>
    /// The resolution and the colour space are passed rather than read from the context,
    /// because the target decides both: a PNG page is always colour, and a raster page is
    /// whatever the printer asked for. The pages are yielded one at a time and encoded as
    /// they arrive, so a long document costs the memory of one page and not of all of them;
    /// each page states the count of the whole render in <see cref="RenderedPdfPage.PageCount"/>.
    /// </remarks>
    protected abstract IAsyncEnumerable<RenderedPdfPage> RenderAsync(
        byte[] pdf,
        PrintConversionContext context,
        int dpi,
        RasterColorSpace colorSpace,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return IsRaster(context.TargetContentType)
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
        var renderDpi = PdfRenderLimits.ClampDpi(context.Dpi);
        List<byte[]> images = [];
        await foreach (var page in RenderAsync(data, context, renderDpi, RasterColorSpace.Srgb8, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var placed = Place(page, context, renderDpi);
            images.Add(PngWriter.Encode(placed.Pixels, placed.Width, placed.Height, PngColorType.Rgb8, context.Dpi));
        }

        return images;
    }

    // One raster stream carries every page, so this answers with a single document and
    // not with one a page. Each page is written as it is rendered, because a document held
    // whole would cost more memory than the job it prints; the writer waits for the first
    // page, which carries the page count the stream header states.
    private async Task<IReadOnlyList<byte[]>> ConvertToRasterAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var renderDpi = PdfRenderLimits.ClampDpi(context.Dpi);
        var colorSpace = PwgRaster.ColorSpaceFor(context.RasterType);
        var isUrf = String.Equals(context.TargetContentType, PrinterContentTypes.Urf, StringComparison.OrdinalIgnoreCase);

        await using MemoryStream document = new();
        RasterWriter? writer = null;
        await foreach (var page in RenderAsync(data, context, renderDpi, colorSpace, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (writer is null)
            {
                RasterOptions options = new()
                {
                    ResolutionDpi = context.Dpi,
                    ColorSpace = colorSpace,
                    TotalPageCount = page.PageCount,
                    Duplex = context.Duplex,
                    SheetBack = PwgRaster.SheetBackFor(context.SheetBack),
                    MediaName = context.MediaName,
                    MediaSizeNames = context.MediaSizeNames,
                };
                writer = isUrf ? new UrfWriter(document, options) : new PwgRasterWriter(document, options);
            }

            var placed = Place(page, context, renderDpi);
            writer.WritePage(placed.Pixels, placed.Width, placed.Height);
        }

        return [document.ToArray()];
    }

    // A channel that named its media gets a page the size of that media, with the fit, the
    // anchor and the offset already in the pixels. One that named none gets the page as it
    // was rendered, and places it itself. Either way the pixels are at the context's
    // resolution, which the header and the canvas are sized from, even where the engine
    // rendered below it.
    private static ComposedPage Place(RenderedPdfPage page, PrintConversionContext context, int renderDpi) =>
        RasterPlacement.Place(page.Pixels, page.Width, page.Height, page.BytesPerPixel, context, renderDpi)
        ?? Rescale(page, context, renderDpi);

    private static ComposedPage Rescale(RenderedPdfPage page, PrintConversionContext context, int renderDpi)
    {
        if (renderDpi == context.Dpi)
        {
            return new ComposedPage(page.Pixels, page.Width, page.Height);
        }

        var width = RasterPlacement.AtDpi(page.Width, renderDpi, context.Dpi);
        var height = RasterPlacement.AtDpi(page.Height, renderDpi, context.Dpi);
        var pixels = RasterCanvas.Compose(
            page.Pixels,
            page.Width,
            page.Height,
            page.BytesPerPixel,
            new RasterTarget
            {
                Width = width,
                Height = height,
                Destination = new ImageRectangle(0, 0, width, height),
                Resampling = RasterPlacement.ResamplingFor(context.Smoothing),
            });
        return new ComposedPage(pixels, width, height);
    }

    private static bool IsRaster(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.PwgRaster, StringComparison.OrdinalIgnoreCase)
        || String.Equals(contentType, PrinterContentTypes.Urf, StringComparison.OrdinalIgnoreCase);
}
