using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
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

    // One page of raw pixels, packed with no padding between the lines.
    internal readonly record struct RasterPage(byte[] Pixels, int Width, int Height);

    // Renders the selected pages in document order, one PNG per page. PageRanges is
    // the 1-based option the caller set; null prints the whole document.
    internal static Task<IReadOnlyList<byte[]>> RenderAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        CancellationToken cancellationToken) =>
        RenderAsync(pdf, dpi, ranges, RenderPageAsync, cancellationToken);

    // The same pages as raw pixels, for a caller that encodes them itself.
    internal static Task<IReadOnlyList<RasterPage>> RenderRasterAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken) =>
        RenderAsync(
            pdf,
            dpi,
            ranges,
            (page, resolution, index, token) => RenderRasterPageAsync(page, resolution, index, colorSpace, token),
            cancellationToken);

    private static async Task<IReadOnlyList<TPage>> RenderAsync<TPage>(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        Func<PdfPage, int, int, CancellationToken, Task<TPage>> render,
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

            List<TPage> rendered = new(selected.Count);
            foreach (var index in selected)
            {
                using var page = document.GetPage((uint)index);
                rendered.Add(await render(page, dpi, index, cancellationToken).ConfigureAwait(false));
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
        using var output = await RenderToStreamAsync(page, dpi, index, BitmapEncoder.PngEncoderId, cancellationToken).ConfigureAwait(false);

        output.Seek(0);
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size).AsTask(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[output.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    // The engine only writes encoded bitmaps, so the page is rendered to an uncompressed
    // BMP and read straight back. GetPixelDataAsync answers with tightly packed pixels,
    // which is what the raster encoders need and what a locked buffer does not promise.
    private static async Task<RasterPage> RenderRasterPageAsync(
        PdfPage page,
        int dpi,
        int index,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken)
    {
        using var output = await RenderToStreamAsync(page, dpi, index, BitmapEncoder.BmpEncoderId, cancellationToken).ConfigureAwait(false);

        output.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(output).AsTask(cancellationToken).ConfigureAwait(false);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);

        var width = (int)decoder.PixelWidth;
        var height = (int)decoder.PixelHeight;
        return new RasterPage(Repack(pixels.DetachPixelData(), width * height, colorSpace), width, height);
    }

    // BGRA to the chunky layout PWG Raster wants: three octets a pixel in red, green, blue
    // order, or one octet of luma. The alpha is ignored, because the page was rendered onto
    // white and carries none.
    private static byte[] Repack(byte[] bgra, int pixelCount, PwgRasterColorSpace colorSpace)
    {
        if (colorSpace == PwgRasterColorSpace.Grayscale8)
        {
            var gray = new byte[pixelCount];
            for (var i = 0; i < pixelCount; i++)
            {
                // Rec. 601 luma, in integer arithmetic.
                gray[i] = (byte)(((bgra[(i * 4) + 2] * 299) + (bgra[(i * 4) + 1] * 587) + (bgra[i * 4] * 114)) / 1000);
            }

            return gray;
        }

        var rgb = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            rgb[i * 3] = bgra[(i * 4) + 2];
            rgb[(i * 3) + 1] = bgra[(i * 4) + 1];
            rgb[(i * 3) + 2] = bgra[i * 4];
        }

        return rgb;
    }

    private static async Task<InMemoryRandomAccessStream> RenderToStreamAsync(
        PdfPage page,
        int dpi,
        int index,
        Guid encoderId,
        CancellationToken cancellationToken)
    {
        var size = page.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} has no size to render.");
        }

        var (width, height) = RenderPixels(size.Width, size.Height, dpi);
        var output = new InMemoryRandomAccessStream();
        PdfPageRenderOptions options = new()
        {
            BitmapEncoderId = encoderId,
            DestinationWidth = width,
            DestinationHeight = height,
        };

        await page.RenderToStreamAsync(output, options).AsTask(cancellationToken).ConfigureAwait(false);
        if (output.Size == 0)
        {
            output.Dispose();
            throw new InvalidOperationException($"The PDF page {index + 1} rendered to nothing.");
        }

        return output;
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
        return (ToPixelCount(width * scale), ToPixelCount(height * scale));
    }

    // A page always renders at least one pixel a side, and never more than the cap:
    // the round up above can pass it by one when the scale lands on it exactly.
    private static uint ToPixelCount(double value) =>
        Math.Max(1u, Math.Min((uint)Math.Ceiling(value), MaxRenderPixels));
}
