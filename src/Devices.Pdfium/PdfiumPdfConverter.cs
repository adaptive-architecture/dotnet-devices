using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Pdfium;

// Reads PDF only. The engine behind it is PDFium, which the package restores as a native
// library for every platform this library runs on.
//
// It writes two formats, because the two channels that convert want different things: the
// Windows spooler draws PNG pages through GDI, and an IPP printer reads PWG Raster and
// never PNG. The target the context names decides which.
//
// It renders through PdfiumDocument, the package's public entry point, so that the surface
// an application places a page with is proved by a caller inside the library.
internal sealed class PdfiumPdfConverter : IPrintPayloadConverter
{
    // The engine, not the package: a job names what renders it, and this reads the same
    // on every platform the package runs on.
    public string Name => "PDFium";

    public bool CanConvert(string contentType) =>
        String.Equals(contentType, PrinterContentTypes.Pdf, StringComparison.OrdinalIgnoreCase);

    public bool CanEmit(string targetContentType) =>
        String.Equals(targetContentType, PrinterContentTypes.Png, StringComparison.OrdinalIgnoreCase)
        || String.Equals(targetContentType, PrinterContentTypes.PwgRaster, StringComparison.OrdinalIgnoreCase);

    // This converter composes onto the media when the channel names one, so the fit is in the
    // pixels and the printer must not apply it a second time.
    public bool PlacesOnMedia(PrintConversionContext context) => RasterPlacement.PlacesOnMedia(context);

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
    private static async Task<IReadOnlyList<byte[]>> ConvertToPngAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var dpi = PdfiumLimits.ClampDpi(context.Dpi);
        var pages = await PdfiumDocument
            .RenderAsync(data, Options(context, dpi, PwgRasterColorSpace.Srgb8), cancellationToken)
            .ConfigureAwait(false);

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
    private static async Task<IReadOnlyList<byte[]>> ConvertToRasterAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var dpi = PdfiumLimits.ClampDpi(context.Dpi);
        var colorSpace = PwgRaster.ColorSpaceFor(context.RasterType);
        var pages = await PdfiumDocument
            .RenderAsync(data, Options(context, dpi, colorSpace), cancellationToken)
            .ConfigureAwait(false);

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

    private static PdfRenderOptions Options(PrintConversionContext context, int dpi, PwgRasterColorSpace colorSpace) =>
        new()
        {
            Dpi = dpi,
            PageRanges = context.PageRanges,
            ColorSpace = colorSpace,
            Smoothing = context.Smoothing != false,
            Password = context.DocumentPassword,
        };

    // A channel that named its media gets a page the size of that media, with the fit, the
    // anchor and the offset already in the pixels. One that named none gets the page as it
    // was rendered, and places it itself.
    private static ComposedPage Place(PdfPage page, PrintConversionContext context) =>
        RasterPlacement.Place(page.Pixels, page.Width, page.Height, page.BytesPerPixel, context)
        ?? new ComposedPage(page.Pixels, page.Width, page.Height);
}
