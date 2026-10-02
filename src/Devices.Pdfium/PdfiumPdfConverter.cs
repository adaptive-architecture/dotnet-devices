using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Pdfium;

// The engine behind this is PDFium, which the package restores as a native library for
// every platform this library runs on. Everything but the engine call is the base class.
//
// It renders through PdfiumDocument, the package's public entry point, so that the surface
// an application places a page with is proved by a caller inside the library. On the Windows
// spooler it draws into the printer device context instead when the job asks for vectors.
internal sealed class PdfiumPdfConverter : PdfPayloadConverter, IPrintDeviceRenderer
{
    // The engine, not the package: a job names what renders it, and this reads the same
    // on every platform the package runs on.
    public override string Name => "PDFium";

    protected override IAsyncEnumerable<RenderedPdfPage> RenderAsync(
        byte[] pdf,
        PrintConversionContext context,
        int dpi,
        RasterColorSpace colorSpace,
        CancellationToken cancellationToken) =>
        PdfiumDocument.RenderAsync(
            pdf,
            new PdfRenderOptions
            {
                Dpi = dpi,
                PageRanges = context.PageRanges,
                ColorSpace = colorSpace,
                Smoothing = context.Smoothing != false,
                Password = context.DocumentPassword,
            },
            cancellationToken);

    public Task<IPrintDeviceDocument> OpenAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return PdfiumRenderer.OpenAsync(
            data,
            new PdfRenderOptions { PageRanges = context.PageRanges, Password = context.DocumentPassword },
            cancellationToken);
    }
}
