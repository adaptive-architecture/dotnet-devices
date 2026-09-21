using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Windows;

// Reads PDF only. The engine behind it is in-box on Windows 10 and later, so the
// converter carries no NuGet dependency of its own.
//
// It writes two formats, because the two channels that convert want different things: the
// Windows spooler draws PNG pages through GDI, and an IPP printer reads PWG Raster and
// never PNG. The target the context names decides which.
[SupportedOSPlatform("windows10.0.10240.0")]
internal sealed class WindowsPdfConverter : IPrintPayloadConverter
{
    // The engine, not the package. "Windows" is what a person picking between this and
    // PDFium would call the one that is already on the machine.
    public string Name => "Windows";

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
            : WindowsPdfRenderer.RenderAsync(data, context.Dpi, context.PageRanges, context.DocumentPassword, cancellationToken);
    }

    // One PWG Raster stream carries every page, so this answers with a single document and
    // not with one a page. Pages are written as they are rendered, because a document held
    // whole would cost more memory than the job it prints.
    private static async Task<IReadOnlyList<byte[]>> ConvertToRasterAsync(
        byte[] data,
        PrintConversionContext context,
        CancellationToken cancellationToken)
    {
        var colorSpace = PwgRaster.ColorSpaceFor(context.RasterType);
        var pages = await WindowsPdfRenderer
            .RenderRasterAsync(data, context.Dpi, context.PageRanges, colorSpace, context.DocumentPassword, cancellationToken)
            .ConfigureAwait(false);

        await using MemoryStream document = new();
        PwgRasterWriter writer = new(document, new PwgRasterOptions
        {
            ResolutionDpi = context.Dpi,
            ColorSpace = colorSpace,
            TotalPageCount = pages.Count,
            Duplex = context.Duplex,
            SheetBack = PwgRaster.SheetBackFor(context.SheetBack),
            MediaName = context.MediaName,
        });

        var bytesPerPixel = colorSpace == PwgRasterColorSpace.Srgb8 ? 3 : 1;
        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The same placement the other engine applies, from the same arithmetic. This
            // engine smooths whatever it is asked, so a job that wanted none still gets a
            // nearest-neighbour composition here, which is the half of it that is ours.
            var placed = RasterPlacement.Place(page.Pixels, page.Width, page.Height, bytesPerPixel, context);
            if (placed is null)
            {
                writer.WritePage(page.Pixels, page.Width, page.Height);
            }
            else
            {
                writer.WritePage(placed.Pixels, placed.Width, placed.Height);
            }
        }

        return [document.ToArray()];
    }
}
