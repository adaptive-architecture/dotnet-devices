using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;

namespace AdaptArch.Devices.Windows;

// The engine behind this is in-box on Windows 10 and later, so the converter carries no
// NuGet dependency of its own. Everything but the engine call is the base class.
[SupportedOSPlatform("windows10.0.10240.0")]
internal sealed class WindowsPdfConverter : PdfPayloadConverter
{
    // The engine, not the package. "Windows" is what a person picking between this and
    // PDFium would call the one that is already on the machine.
    public override string Name => "Windows";

    // This engine smooths whatever it is asked, so a job that wanted none still gets
    // smoothed glyphs here; only the composition the base class applies is nearest
    // neighbour, which is the half of it that is ours.
    protected override Task<IReadOnlyList<RenderedPdfPage>> RenderAsync(
        byte[] pdf,
        PrintConversionContext context,
        int dpi,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken) =>
        WindowsPdfRenderer.RenderAsync(pdf, dpi, context.PageRanges, colorSpace, context.DocumentPassword, cancellationToken);
}
