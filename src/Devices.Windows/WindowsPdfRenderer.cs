using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace AdaptArch.Devices.Windows;

// Renders PDF pages to raw pixels with the in-box Windows.Data.Pdf engine, so the spooler
// PDF path needs no extra package. The caller encodes them, which is what keeps this file
// down to the engine calls and leaves the rest to PdfPayloadConverter. The targeting pack
// is build-time metadata only, and the platform check on entry guards every call below.
[SupportedOSPlatform("windows10.0.10240.0")]
internal static class WindowsPdfRenderer
{
    // Renders the selected pages in document order. Ranges is the 1-based option the
    // caller set; null renders the whole document.
    internal static async Task<IReadOnlyList<RenderedPdfPage>> RenderAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        PwgRasterColorSpace colorSpace,
        string? password,
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
        dpi = PdfRenderLimits.ClampDpi(dpi);

        try
        {
            using var source = new InMemoryRandomAccessStream();
            await source.WriteAsync(pdf.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
            source.Seek(0);

            // The engine takes the password on the load and reports a wrong one the same way
            // it reports a corrupt file, so the two are told apart by what the job carried.
            var document = password is null
                ? await PdfDocument.LoadFromStreamAsync(source).AsTask(cancellationToken).ConfigureAwait(false)
                : await PdfDocument.LoadFromStreamAsync(source, password).AsTask(cancellationToken).ConfigureAwait(false);
            if (document.PageCount == 0)
            {
                throw new InvalidOperationException("The PDF has no pages to print.");
            }

            var selected = PageRange.Select((int)document.PageCount, ranges);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The page ranges select no page of this PDF.");
            }

            List<RenderedPdfPage> rendered = new(selected.Count);
            foreach (var index in selected)
            {
                using var page = document.GetPage((uint)index);
                rendered.Add(await RenderPageAsync(page, dpi, index, colorSpace, cancellationToken).ConfigureAwait(false));
            }

            return rendered;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            // WinRT reports a corrupt file and a wrong password as the same COM fault, which
            // names nothing the caller can act on. What the job carried is the only thing that
            // separates them here, so it is what the message goes on.
            throw new InvalidOperationException(
                password is null
                    ? "The PDF could not be read. It may be corrupt, or password-protected and the job carried no password."
                    : "The PDF could not be read. It may be corrupt, or the password the job carried does not open it.",
                exception);
        }
    }

    // The engine only writes encoded bitmaps, so the page is rendered to an uncompressed
    // BMP and read straight back. GetPixelDataAsync answers with tightly packed pixels,
    // which is what the encoders need and what a locked buffer does not promise.
    private static async Task<RenderedPdfPage> RenderPageAsync(
        PdfPage page,
        int dpi,
        int index,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken)
    {
        var size = page.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} has no size to render.");
        }

        var (width, height) = PdfRenderLimits.RenderPixels(size.Width, size.Height, dpi, PdfRenderLimits.DipsPerInch);
        using var output = await RenderToStreamAsync(page, width, height, index, cancellationToken).ConfigureAwait(false);

        output.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(output).AsTask(cancellationToken).ConfigureAwait(false);
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);

        var decodedWidth = (int)decoder.PixelWidth;
        var decodedHeight = (int)decoder.PixelHeight;

        // The page box goes out in points whatever the engine measured it in, because a
        // rendered page describes itself the same way whichever engine drew it.
        const double PointsPerDip = PdfRenderLimits.PointsPerInch / PdfRenderLimits.DipsPerInch;
        return new RenderedPdfPage(
            Repack(pixels.DetachPixelData(), decodedWidth * decodedHeight, colorSpace),
            decodedWidth,
            decodedHeight,
            colorSpace,
            size.Width * PointsPerDip,
            size.Height * PointsPerDip);
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
        int width,
        int height,
        int index,
        CancellationToken cancellationToken)
    {
        var output = new InMemoryRandomAccessStream();
        PdfPageRenderOptions options = new()
        {
            BitmapEncoderId = BitmapEncoder.BmpEncoderId,
            DestinationWidth = (uint)width,
            DestinationHeight = (uint)height,
        };

        await page.RenderToStreamAsync(output, options).AsTask(cancellationToken).ConfigureAwait(false);
        if (output.Size == 0)
        {
            output.Dispose();
            throw new InvalidOperationException($"The PDF page {index + 1} rendered to nothing.");
        }

        return output;
    }
}
