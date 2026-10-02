using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using PDFiumCore;

namespace AdaptArch.Devices.Pdfium;

// Renders PDF pages to raw pixels with PDFium, the engine behind Chrome and Edge. The
// caller encodes them, because the two channels that convert want different formats and
// PDFium writes neither.
//
// It also draws them straight into a Windows printer device context, which PDFiumCore does
// not bind.
//
// This is the only file that calls the engine, and every call in it runs under Gate.
internal static partial class PdfiumRenderer
{
    // public/fpdfview.h: the two bitmap formats this renderer asks for. Gray is one octet a
    // pixel, and BGR is three in blue, green, red order, which is the one swap below.
    private const int GrayFormat = 1;
    private const int BgrFormat = 2;

    // Render as the page would print rather than as it would appear on screen, which is what
    // this library is for. The names are PDFiumCore's for the FPDF_* flags of fpdfview.h.
    private const int PrintingFlags = (int)RenderFlags.RenderForPrinting;

    // Everything PDFium smooths. Text also loses its LCD optimization, which is a screen
    // trick that means nothing on paper.
    private const int NoSmoothingFlags = (int)RenderFlags.DisableTextAntialiasing
        | (int)RenderFlags.DisableImageAntialiasing
        | (int)RenderFlags.DisablePathAntialiasing;

    // Opaque white, as FPDFBitmapFillRect takes it: alpha, red, green, blue.
    private const ulong White = 0xFFFFFFFF;

    private const int NoRotation = 0;

    // public/fpdfview.h FPDF_ERR_PASSWORD: the document opened with the wrong password, or
    // with none where one was needed. It is worth telling apart from a corrupt file, because
    // only one of the two is something the caller can do anything about.
    private const int PasswordError = 4;

    // PDFium is not thread-safe, and PrinterManager may call a converter for two jobs at
    // once. One gate around every call serializes the whole library, including the one-time
    // initialization below, which therefore needs no lock of its own. A document stays open
    // between calls without the gate: PDFium forbids calls that overlap, not documents that do.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool s_initialized;

    // Renders the selected pages in document order, one at a time as the caller takes them,
    // so a long document costs one page of memory. PageRanges is the 1-based option the
    // caller set; null renders the whole document. The gate is taken for the open, for each
    // page and for the close, and the document stays open in between.
    internal static async IAsyncEnumerable<RenderedPdfPage> RenderAsync(
        byte[] pdf,
        PdfRenderOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        OpenedPdf opened;
        try
        {
            opened = Open(pdf, options);
        }
        finally
        {
            _ = Gate.Release();
        }

        try
        {
            var dpi = PdfRenderLimits.ClampDpi(options.Dpi);
            var flags = options.Smoothing ? PrintingFlags : PrintingFlags | NoSmoothingFlags;
            foreach (var index in opened.Selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                RenderedPdfPage page;
                try
                {
                    page = RenderPage(opened.Document, index, dpi, options.ColorSpace, flags) with { PageCount = opened.Selected.Count };
                }
                finally
                {
                    _ = Gate.Release();
                }

                yield return page;
            }
        }
        finally
        {
            using var hold = Hold();
            opened.Dispose();
        }
    }

    // Opens the document under the gate and hands it back with the gate released: its pages
    // are drawn later, one at a time, inside the print job, and each draw takes the gate for
    // itself. A job that is slow to spool therefore holds up no other render, and a caller
    // that never disposes the document leaks the document and not the engine.
    internal static async Task<IPrintDeviceDocument> OpenAsync(
        byte[] pdf,
        PdfRenderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(options);

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return new DeviceDocument(Open(pdf, options));
        }
        finally
        {
            _ = Gate.Release();
        }
    }

    // The gate for one synchronous call into the engine. The default hold holds nothing, for
    // a call made by a caller that already has the gate.
    private static GateHold Hold()
    {
        Gate.Wait();
        return new GateHold(true);
    }

    private readonly struct GateHold : IDisposable
    {
        private readonly bool _held;

        public GateHold(bool held) => _held = held;

        public void Dispose()
        {
            if (_held)
            {
                _ = Gate.Release();
            }
        }
    }

    private static OpenedPdf Open(byte[] pdf, PdfRenderOptions options)
    {
        if (!s_initialized)
        {
            // Deliberately never paired with FPDF_DestroyLibrary: the library is
            // process-wide, and tearing it down would break any other consumer of PDFium
            // in the same process.
            fpdfview.FPDF_InitLibrary();
            s_initialized = true;
        }

        // FPDF_LoadMemDocument64 does not copy, and PDFium reads the buffer for as long as
        // the document is open, so the bytes stay pinned until it is closed.
        var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        FpdfDocumentT? document = null;
        try
        {
            document = fpdfview.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (ulong)pdf.Length, options.Password);
            if (document is null)
            {
                var error = fpdfview.FPDF_GetLastError();
                if (error == PasswordError)
                {
                    throw new InvalidOperationException(
                        options.Password is null
                            ? "The PDF is password-protected and the job carried no password."
                            : "The password the job carried does not open this PDF.");
                }

                throw new InvalidOperationException(
                    $"The PDF could not be read. It may be corrupt. PDFium reported error {error}.");
            }

            var pageCount = fpdfview.FPDF_GetPageCount(document);
            if (pageCount == 0)
            {
                throw new InvalidOperationException("The PDF has no pages to print.");
            }

            var selected = PageRange.Select(pageCount, options.PageRanges);
            if (selected.Count == 0)
            {
                throw new InvalidOperationException("The page ranges select no page of this PDF.");
            }

            return new OpenedPdf(pinned, document, selected);
        }
        catch
        {
            if (document is not null)
            {
                fpdfview.FPDF_CloseDocument(document);
            }

            pinned.Free();
            throw;
        }
    }

    private static RenderedPdfPage RenderPage(FpdfDocumentT document, int index, int dpi, RasterColorSpace colorSpace, int flags)
    {
        using FS_SIZEF_ size = new();
        if (fpdfview.FPDF_GetPageSizeByIndexF(document, index, size) == 0)
        {
            throw new InvalidOperationException($"The PDF page {index + 1} has no size to render.");
        }

        (var width, var height) = PdfRenderLimits.RenderPixels(size.Width, size.Height, dpi, PdfRenderLimits.PointsPerInch);
        var isColor = colorSpace == RasterColorSpace.Srgb8;
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

            fpdfview.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, NoRotation, flags);
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

        return new RenderedPdfPage(pixels, width, height, colorSpace, size.Width, size.Height);
    }

    // public/fpdfview.h, Windows only. PDFiumCore binds the bitmap renderer and not this one.
    [LibraryImport("pdfium", EntryPoint = "FPDF_RenderPage")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [SupportedOSPlatform("windows")]
    private static partial int RenderPageToDevice(nint deviceContext, nint page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    // PDFium writes blue, green, red; both encoders read red, green, blue. The green stays
    // where it is, so only the two outer octets of each pixel move.
    private static void SwapBlueAndRed(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 3)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    // A loaded document, the pinned bytes it reads and the pages selected from it.
    private sealed class OpenedPdf : IDisposable
    {
        private GCHandle _pinned;

        public OpenedPdf(GCHandle pinned, FpdfDocumentT document, IReadOnlyList<int> selected)
        {
            _pinned = pinned;
            Document = document;
            Selected = selected;
        }

        public FpdfDocumentT Document { get; }

        public IReadOnlyList<int> Selected { get; }

        // A document the caller forgot is closed by the finalizer, under the gate as every call
        // is, so the leak costs the document and its pinned bytes until the next collection
        // and nothing else.
        ~OpenedPdf() => Close(true);

        public void Dispose()
        {
            Close(false);
            GC.SuppressFinalize(this);
        }

        private void Close(bool finalizing)
        {
            using var hold = finalizing ? Hold() : default;
            fpdfview.FPDF_CloseDocument(Document);
            _pinned.Free();
        }
    }

    // Holds the gate from the moment it is opened to the moment it is disposed, so nothing
    // else enters the engine while a print job draws its pages.
    private sealed class DeviceDocument : IPrintDeviceDocument
    {
        private readonly OpenedPdf _pdf;
        private bool _disposed;

        public DeviceDocument(OpenedPdf pdf) => _pdf = pdf;

        public int PageCount => _pdf.Selected.Count;

        public MediaDimensions PageSize(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var hold = Hold();
            using FS_SIZEF_ size = new();
            if (fpdfview.FPDF_GetPageSizeByIndexF(_pdf.Document, _pdf.Selected[index], size) == 0)
            {
                throw new InvalidOperationException($"The PDF page {_pdf.Selected[index] + 1} has no size to render.");
            }

            return new MediaDimensions(
                PrintLength.FromInches(size.Width / PdfRenderLimits.PointsPerInch),
                PrintLength.FromInches(size.Height / PdfRenderLimits.PointsPerInch));
        }

        public void Draw(nint deviceContext, int index, ImageRectangle target, int quarterTurns, bool smoothing)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("PDFium draws into a device context only on Windows.");
            }

            using var hold = Hold();
            var number = _pdf.Selected[index] + 1;
            var page = fpdfview.FPDF_LoadPage(_pdf.Document, _pdf.Selected[index])
                ?? throw new InvalidOperationException(
                    $"The PDF page {number} could not be read. PDFium reported error {fpdfview.FPDF_GetLastError()}.");
            try
            {
                var flags = smoothing ? PrintingFlags : PrintingFlags | NoSmoothingFlags;
                if (RenderPageToDevice(deviceContext, page.__Instance, target.X, target.Y, target.Width, target.Height, quarterTurns, flags) == 0)
                {
                    throw new InvalidOperationException($"PDFium could not draw the PDF page {number} into the printer device context.");
                }
            }
            finally
            {
                fpdfview.FPDF_ClosePage(page);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            using var hold = Hold();
            _pdf.Dispose();
        }
    }
}
