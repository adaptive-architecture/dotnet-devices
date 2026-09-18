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
            : WindowsPdfRenderer.RenderAsync(data, context.Dpi, context.PageRanges, cancellationToken);
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
            .RenderRasterAsync(data, context.Dpi, context.PageRanges, colorSpace, cancellationToken)
            .ConfigureAwait(false);

        await using MemoryStream document = new();
        PwgRasterWriter writer = new(document, new PwgRasterOptions
        {
            ResolutionDpi = context.Dpi,
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
