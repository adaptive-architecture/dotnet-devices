using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Pdfium;

// Reads PDF only. The engine behind it is PDFium, which the package restores as a native
// library for every platform this library runs on.
//
// It writes two formats, because the two channels that convert want different things: the
// Windows spooler draws PNG pages through GDI, and an IPP printer reads PWG Raster and
// never PNG. The target the context names decides which.
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
        var pages = await PdfiumRenderer
            .RenderAsync(data, dpi, context.PageRanges, PwgRasterColorSpace.Srgb8, cancellationToken)
            .ConfigureAwait(false);

        List<byte[]> images = new(pages.Count);
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            images.Add(PngWriter.Encode(page.Pixels, page.Width, page.Height, PngColorType.Rgb8, dpi));
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
        var pages = await PdfiumRenderer
            .RenderAsync(data, dpi, context.PageRanges, colorSpace, cancellationToken)
            .ConfigureAwait(false);

        await using MemoryStream document = new();
        PwgRasterWriter writer = new(document, new PwgRasterOptions
        {
            ResolutionDpi = dpi,
            ColorSpace = colorSpace,
            TotalPageCount = pages.Count,
            Duplex = context.Duplex,
            SheetBack = PwgRaster.SheetBackFor(context.SheetBack),
        });

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WritePage(page.Pixels, page.Width, page.Height);
        }

        return [document.ToArray()];
    }
}
