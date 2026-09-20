using System.Runtime.InteropServices;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using PDFiumCore;

namespace AdaptArch.Devices.Pdfium;

// Renders PDF pages to raw pixels with PDFium, the engine behind Chrome and Edge. The
// caller encodes them, because the two channels that convert want different formats and
// PDFium writes neither.
//
// This is the only file that calls the engine, and every call in it runs under Gate.
internal static class PdfiumRenderer
{
    // public/fpdfview.h: the two bitmap formats this renderer asks for. Gray is one octet a
    // pixel, and BGR is three in blue, green, red order, which is the one swap below.
    private const int GrayFormat = 1;
    private const int BgrFormat = 2;

    // public/fpdfview.h FPDF_PRINTING: render as the page would print rather than as it
    // would appear on screen, which is what this library is for.
    private const int PrintingFlag = 0x800;

    // Opaque white, as FPDFBitmapFillRect takes it: alpha, red, green, blue.
    private const ulong White = 0xFFFFFFFF;

    private const int NoRotation = 0;

    // PDFium is not thread-safe, and PrinterManager may call a converter for two jobs at
    // once. One gate around every call serializes the whole library, including the one-time
    // initialization below, which therefore needs no lock of its own.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool s_initialized;

    // One page of raw pixels, packed with no padding between the lines.
    internal readonly record struct RasterPage(byte[] Pixels, int Width, int Height);

    // Renders the selected pages in document order. PageRanges is the 1-based option the
    // caller set; null renders the whole document.
    internal static async Task<IReadOnlyList<RasterPage>> RenderAsync(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Render(pdf, dpi, ranges, colorSpace, cancellationToken);
        }
        finally
        {
            _ = Gate.Release();
        }
    }

    private static List<RasterPage> Render(
        byte[] pdf,
        int dpi,
        IReadOnlyList<PageRange>? ranges,
        PwgRasterColorSpace colorSpace,
        CancellationToken cancellationToken)
    {
        if (!s_initialized)
        {
            // Deliberately never paired with FPDF_DestroyLibrary: the library is
            // process-wide, and tearing it down would break any other consumer of PDFium
            // in the same process.
            fpdfview.FPDF_InitLibrary();
            s_initialized = true;
        }

        dpi = PdfiumLimits.ClampDpi(dpi);

        // FPDF_LoadMemDocument64 does not copy, and PDFium reads the buffer for as long as
        // the document is open, so the bytes stay pinned until it is closed.
        var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        FpdfDocumentT? document = null;
        try
        {
            document = fpdfview.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (ulong)pdf.Length, null);
            if (document is null)
            {
                throw new InvalidOperationException(
                    $"The PDF could not be read. It may be corrupt or password-protected. PDFium reported error {fpdfview.FPDF_GetLastError()}.");
            }

            var pageCount = fpdfview.FPDF_GetPageCount(document);
            if (pageCount == 0)
            {
                throw new InvalidOperationException("The PDF has no pages to print.");
            }

            var selected = PageRange.Select(pageCount, ranges);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The page ranges select no page of this PDF.");
            }

            List<RasterPage> rendered = new(selected.Count);
            foreach (var index in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                rendered.Add(RenderPage(document, index, dpi, colorSpace));
            }

            return rendered;
        }
        finally
        {
            if (document is not null)
            {
                fpdfview.FPDF_CloseDocument(document);
            }

            pinned.Free();
        }
    }

    private static RasterPage RenderPage(FpdfDocumentT document, int index, int dpi, PwgRasterColorSpace colorSpace)
    {
        using FS_SIZEF_ size = new();
        if (fpdfview.FPDF_GetPageSizeByIndexF(document, index, size) == 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} has no size to render.");
        }

        (var width, var height) = PdfiumLimits.RenderPixels(size.Width, size.Height, dpi);
        var isColor = colorSpace == PwgRasterColorSpace.Srgb8;
        var stride = width * (isColor ? 3 : 1);
        var pixels = new byte[stride * height];

        // The buffer is ours and the stride is the packed one both encoders take, so PDFium
        // pads nothing and nothing is repacked afterwards. It also does not clear what it
        // renders onto, and an unfilled buffer prints whatever the allocator left there.
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        FpdfBitmapT? bitmap = null;
        FpdfPageT? page = null;
        try
        {
            bitmap = fpdfview.FPDFBitmapCreateEx(width, height, isColor ? BgrFormat : GrayFormat, pinned.AddrOfPinnedObject(), stride);
            if (bitmap is null)
            {
                throw new InvalidOperationException($"The PDF page {index + 1} is {width} by {height} pixels, which PDFium would not allocate.");
            }

            _ = fpdfview.FPDFBitmapFillRect(bitmap, 0, 0, width, height, White);

            page = fpdfview.FPDF_LoadPage(document, index);
            if (page is null)
            {
                throw new InvalidOperationException(
                    $"The PDF page {index + 1} could not be read. PDFium reported error {fpdfview.FPDF_GetLastError()}.");
            }

            fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, NoRotation, PrintingFlag);
        }
        finally
        {
            if (page is not null)
            {
                fpdfview.FPDF_ClosePage(page);
            }

            if (bitmap is not null)
            {
                fpdfview.FPDFBitmapDestroy(bitmap);
            }

            pinned.Free();
        }

        if (isColor)
        {
            SwapBlueAndRed(pixels);
        }

        return new RasterPage(pixels, width, height);
    }

    // PDFium writes blue, green, red; both encoders read red, green, blue. The green stays
    // where it is, so only the two outer octets of each pixel move.
    private static void SwapBlueAndRed(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 3)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }
}
