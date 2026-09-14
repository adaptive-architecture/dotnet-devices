using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AdaptArch.Devices.Printing.Spooler;

// Renders PDF pages to PNG with the in-box Windows.Data.Pdf engine, so the spooler
// PDF path needs no extra package. The targeting pack is build-time metadata only;
// every call below is guarded by the platform check on entry.
[SupportedOSPlatform("windows10.0.10240.0")]
internal static class WindowsPdfRenderer
{
    // PdfPage.Size counts device-independent pixels (1/96 inch).
    private const double DipsPerInch = 96.0;

    // One rendered dimension never exceeds this, whatever the units turn out to be,
    // so a poster-size page cannot exhaust the memory of an inkjet job.
    private const uint MaxRenderPixels = 4960;

    // Renders the selected pages in document order, one PNG per page. PageRanges is
    // the 1-based option the caller set; null prints the whole document.
    internal static async Task<IReadOnlyList<byte[]>> RenderAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("PDF rendering needs the in-box Windows engine.");
        }

        cancellationToken.ThrowIfCancellationRequested();

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

            var selected = WindowsSpoolerContent.SelectPages((int)document.PageCount, ranges);
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

        using var output = new InMemoryRandomAccessStream();
        PdfPageRenderOptions options = new()
        {
            BitmapEncoderId = BitmapEncoder.PngEncoderId,
            DestinationWidth = RenderPixels(size.Width, dpi),
            DestinationHeight = RenderPixels(size.Height, dpi),
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

    private static uint RenderPixels(double dips, int dpi) =>
        Math.Max(1u, Math.Min((uint)Math.Ceiling(dips * dpi / DipsPerInch), MaxRenderPixels));
}
