using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AdaptArch.Devices.Windows;

// Renders PDF pages to PNG with the in-box Windows.Data.Pdf engine, so the spooler
// PDF path needs no extra package. The targeting pack is build-time metadata only,
// and the platform check on entry guards every call below.
[SupportedOSPlatform("windows10.0.10240.0")]
internal static class WindowsPdfRenderer
{
    // PdfPage.Size counts device-independent pixels (1/96 inch).
    private const double DipsPerInch = 96.0;

    // No rendered side exceeds this, whatever the units turn out to be, so a
    // poster-size page cannot exhaust the memory of an inkjet job. The number is the
    // long side of Legal at MaxDpi, so every common office medium renders at the full
    // resolution asked for. A page above it renders smaller than that resolution, and
    // PrintScaling.None then prints it smaller than its own size, because the spooler
    // sizes a converted page from the resolution it asked the converter for.
    private const uint MaxRenderPixels = 8400;

    // What this engine renders well: below the first a page turns to mush, above the
    // second an A4 page needs more memory than an inkjet job should hold. The band is
    // stated here, and not on the spooler path, because it describes this engine only.
    private const int MinDpi = 150;
    private const int MaxDpi = 600;

    // Renders the selected pages in document order, one PNG per page. PageRanges is
    // the 1-based option the caller set; null prints the whole document.
    internal static async Task<IReadOnlyList<byte[]>> RenderAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        // The version is checked, and not only the platform: .NET 10 still supports Windows
        // Server 2012 R2, which has no such engine, and a Server Core install may have none
        // either. Without this the WinRT activation fails inside the render below, where the
        // catch blames the file.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            throw new PlatformNotSupportedException(
                "PDF rendering needs the in-box Windows engine, which ships on Windows 10, on Windows 11 " +
                "and on Windows Server with the Desktop Experience. This machine has none.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        dpi = Math.Clamp(dpi, MinDpi, MaxDpi);

        try
        {
            using var source = new InMemoryRandomAccessStream();
            await source.WriteAsync(pdf.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
            source.Seek(0);

            var document = await PdfDocument.LoadFromStreamAsync(source).AsTask(cancellationToken).ConfigureAwait(false);
            if (document.PageCount == 0)
            {
                throw new InvalidOperationException("The PDF has no pages to print.");
            }

            var selected = PageRange.Select((int)document.PageCount, ranges);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The page ranges select no page of this PDF.");
            }

            List<byte[]> rendered = new(selected.Count);
            foreach (var index in selected)
            {
                using var page = document.GetPage((uint)index);
                rendered.Add(await RenderPageAsync(page, dpi, index, cancellationToken).ConfigureAwait(false));
            }

            return rendered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            // WinRT reports a corrupt or password-protected file as a COM fault, which
            // names nothing the caller can act on.
            throw new InvalidOperationException(
                "The PDF could not be read. It may be corrupt or password-protected.", exception);
        }
    }

    private static async Task<byte[]> RenderPageAsync(PdfPage page, int dpi, int index, CancellationToken cancellationToken)
    {
        var size = page.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} has no size to render.");
        }

        var (width, height) = RenderPixels(size.Width, size.Height, dpi);
        using var output = new InMemoryRandomAccessStream();
        PdfPageRenderOptions options = new()
        {
            BitmapEncoderId = BitmapEncoder.PngEncoderId,
            DestinationWidth = width,
            DestinationHeight = height,
        };
        await page.RenderToStreamAsync(output, options).AsTask(cancellationToken).ConfigureAwait(false);

        if (output.Size == 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} rendered to nothing.");
        }

        output.Seek(0);
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size).AsTask(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[output.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    // The cap belongs to the longer side, and both sides take the same factor. Capping
    // each side on its own would make an A4 page at 600 dots per inch square, because
    // both sides are then above the cap and both stop at it.
    private static (uint Width, uint Height) RenderPixels(double widthDips, double heightDips, int dpi)
    {
        var width = widthDips * dpi / DipsPerInch;
        var height = heightDips * dpi / DipsPerInch;
        var longest = Math.Max(width, height);
        var scale = longest > MaxRenderPixels ? MaxRenderPixels / longest : 1.0;
        return (Pixels(width * scale), Pixels(height * scale));
    }

    // A page always renders at least one pixel a side, and never more than the cap:
    // the round up above can pass it by one when the scale lands on it exactly.
    private static uint Pixels(double value) =>
        Math.Max(1u, Math.Min((uint)Math.Ceiling(value), MaxRenderPixels));
}
