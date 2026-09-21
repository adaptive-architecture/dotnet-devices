using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Prints PNG or JPEG pages through the Windows printer driver with GDI,
// so the driver rasterises every page. The RAW spooler path passes bytes
// unchanged and cannot do this; a printer whose firmware reads only its own
// page language prints nothing for an image sent that way. One job carries
// every page, so a PDF renders once and prints as one document.
// No platform attribute: every native call goes through IWindowsGdiInterop, so the page
// loop, the layout arithmetic and the teardown order run under test anywhere.
internal sealed class WindowsGdiImagePrinter : IWindowsGdiImagePrinter
{
    private readonly IWindowsGdiInterop _gdi;

    public WindowsGdiImagePrinter()
        : this(WindowsGdiInteropAdapter.Instance)
    {
    }

    internal WindowsGdiImagePrinter(IWindowsGdiInterop gdi)
    {
        ArgumentNullException.ThrowIfNull(gdi);
        _gdi = gdi;
    }

    // wingdi.h DM_COPIES. The GDI path honours the copy count in the device
    // mode, so one job prints every copy. The RAW path loops instead, because
    // a RAW queue never reads this field.
    private const uint DmCopies = 0x00000100;

    // GDI+ status Ok.
    private const int GdiplusOk = 0;

    public int Print(WindowsGdiJob job, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return PrintPages(job, [bytes]);
    }

    public int PrintPages(WindowsGdiJob job, IReadOnlyList<byte[]> pages)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.QueueName);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(job.Extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.JobName);
        if (pages.Count == 0)
        {
            throw new ArgumentException("A job prints at least one page.", nameof(pages));
        }

        return PrintFile(job, pages);
    }

    private int PrintFile(WindowsGdiJob job, IReadOnlyList<byte[]> pages)
    {
        var queueName = job.QueueName;
        ApplyCopies(job.DeviceMode, job.Copies);

        var input = WindowsGdiInterop.StartupInput.Version1();
        var startupStatus = _gdi.Startup(out var token, input, out _);
        if (startupStatus != GdiplusOk)
        {
            throw new InvalidOperationException(
                $"Printing '{queueName}' failed to start GDI+ with status {startupStatus}.");
        }

        var deviceContext = IntPtr.Zero;
        var documentStarted = false;
        try
        {
            deviceContext = _gdi.CreateDC(null, queueName, null, job.DeviceMode);
            if (deviceContext == IntPtr.Zero)
            {
                ThrowLastError("CreateDC");
            }

            var printableWidth = _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.HorzRes);
            var printableHeight = _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.VertRes);
            var sheetWidth = _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalWidth);
            var sheetHeight = _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalHeight);
            var page = new PrinterPage(
                printableWidth,
                printableHeight,
                _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.LogPixelsX),
                _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.LogPixelsY),
                // A driver that reports no sheet keeps its margins, which is the answer
                // every printer but a borderless one gives anyway.
                sheetWidth > 0 && sheetHeight > 0 && sheetWidth <= printableWidth && sheetHeight <= printableHeight,
                sheetWidth,
                sheetHeight,
                _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalOffsetX),
                _gdi.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalOffsetY));
            if (page.Width <= 0 || page.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' reported an unusable page of {page.Width}x{page.Height} pixels.");
            }

            var docNamePtr = Marshal.StringToHGlobalUni(job.JobName);
            try
            {
                WindowsGdiInterop.DocInfo docInfo = new()
                {
                    Size = Marshal.SizeOf<WindowsGdiInterop.DocInfo>(),
                    DocName = docNamePtr,
                };
                var jobId = _gdi.StartDoc(deviceContext, docInfo);
                if (jobId <= 0)
                {
                    ThrowLastError("StartDoc");
                }

                documentStarted = true;
                foreach (var pageBytes in pages)
                {
                    DrawPage(deviceContext, job, pageBytes, page);
                }

                if (_gdi.EndDoc(deviceContext) <= 0)
                {
                    ThrowLastError("EndDoc");
                }

                documentStarted = false;
                return jobId;
            }
            finally
            {
                Marshal.FreeHGlobal(docNamePtr);
            }
        }
        finally
        {
            if (documentStarted)
            {
                _ = _gdi.AbortDoc(deviceContext);
            }

            if (deviceContext != IntPtr.Zero)
            {
                _ = _gdi.DeleteDC(deviceContext);
            }

            _gdi.Shutdown(token);
        }
    }

    // One page of the job: decode, lay out, draw, spool. A failure here leaves the
    // page open, so the caller aborts the whole document and no truncated page commits.
    private void DrawPage(
        nint deviceContext,
        WindowsGdiJob job,
        byte[] bytes,
        PrinterPage page)
    {
        var queueName = job.QueueName;
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{job.Extension}");
        File.WriteAllBytes(temporaryPath, bytes);
        var image = IntPtr.Zero;
        var graphics = IntPtr.Zero;
        var pageStarted = false;
        try
        {
            var loadStatus = _gdi.LoadImageFromFile(temporaryPath, out image);
            if (loadStatus != GdiplusOk)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' could not decode the image with GDI+ status {loadStatus}.");
            }

            _ = CheckGdiplus(_gdi.GetImageWidth(image, out var width), queueName);
            _ = CheckGdiplus(_gdi.GetImageHeight(image, out var height), queueName);
            _ = CheckGdiplus(_gdi.GetImageHorizontalResolution(image, out var sourceDpiX), queueName);
            _ = CheckGdiplus(_gdi.GetImageVerticalResolution(image, out var sourceDpiY), queueName);

            // A converted page carries the resolution it was rendered at, because the
            // encoder writes none of its own and the image would claim 96. A page the
            // caller sent keeps what its own file declares.
            var sourceX = job.SourceDpi ?? sourceDpiX;
            var sourceY = job.SourceDpi ?? sourceDpiY;

            // The area the page is fitted into and anchored against, in the coordinates the
            // device context draws in: those start at the printable corner, so the whole
            // sheet begins at minus the offset of that corner inside it.
            var area = page.AreaFor(job.FitArea);
            var placed = WindowsGdiImageLayout.Compute(
                WindowsGdiImageLayout.NaturalPixels((int)width, sourceX, page.DpiX),
                WindowsGdiImageLayout.NaturalPixels((int)height, sourceY, page.DpiY),
                area.Width,
                area.Height,
                job.Orientation,
                job.Scaling,
                new WindowsGdiImageLayout.DeviceLayout
                {
                    Borderless = page.Borderless || job.FitArea == PrintFitArea.Physical,
                    Placement = job.Placement,
                    DpiX = page.DpiX,
                    DpiY = page.DpiY,
                });
            var layout = new ImageRectangle(placed.X + area.X, placed.Y + area.Y, placed.Width, placed.Height);
            if (layout.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' found an empty image of {width}x{height} pixels.");
            }

            if (_gdi.StartPage(deviceContext) <= 0)
            {
                ThrowLastError("StartPage");
            }

            pageStarted = true;
            _ = CheckGdiplus(_gdi.CreateGraphics(deviceContext, out graphics), queueName);
            _ = CheckGdiplus(_gdi.SetPageUnit(graphics, WindowsGdiInterop.UnitPixel), queueName);

            // A job that asked for no smoothing takes the nearest source pixel, which is what
            // keeps the edge of a bar hard when the driver scales the page.
            if (job.Smoothing == false)
            {
                _ = CheckGdiplus(
                    _gdi.SetInterpolationMode(graphics, WindowsGdiInterop.InterpolationNearestNeighbor),
                    queueName);
                _ = CheckGdiplus(_gdi.SetPixelOffsetMode(graphics, WindowsGdiInterop.PixelOffsetHalf), queueName);
            }

            var angle = WindowsGdiImageLayout.RotationDegrees(job.Orientation);
            if (angle != 0f)
            {
                // The turn is about the centre of the area the page was fitted to, which is
                // the same fixed point the layout measured its anchor from. Turning about
                // anything else would move an anchored page as soon as it was rotated.
                var centerX = area.X + (area.Width / 2f);
                var centerY = area.Y + (area.Height / 2f);
                _ = CheckGdiplus(
                    _gdi.TranslateWorldTransform(graphics, centerX, centerY, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    _gdi.RotateWorldTransform(graphics, angle, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    _gdi.TranslateWorldTransform(graphics, -centerX, -centerY, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
            }

            _ = CheckGdiplus(
                _gdi.DrawImageRect(graphics, image, layout.X, layout.Y, layout.Width, layout.Height),
                queueName);

            _ = CheckGdiplus(_gdi.DeleteGraphics(graphics), queueName);
            graphics = IntPtr.Zero;

            if (_gdi.EndPage(deviceContext) <= 0)
            {
                ThrowLastError("EndPage");
            }

            pageStarted = false;
        }
        finally
        {
            if (graphics != IntPtr.Zero)
            {
                _ = _gdi.DeleteGraphics(graphics);
            }

            if (pageStarted)
            {
                _ = _gdi.EndPage(deviceContext);
            }

            if (image != IntPtr.Zero)
            {
                _ = _gdi.DisposeImage(image);
            }

            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // The page is already spooled; a leftover temporary file must not fail it.
            }
            catch (UnauthorizedAccessException)
            {
                // Same as above: the print stands even when cleanup loses.
            }
        }
    }

    // The copy count is the one option the RAW path cannot carry, so the GDI
    // path writes it here instead of looping over separate jobs.
    private static void ApplyCopies(nint deviceMode, int copies)
    {
        if (deviceMode == IntPtr.Zero || copies <= 1)
        {
            return;
        }

        var mode = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(deviceMode);
        mode.Fields |= DmCopies;
        mode.Copies = (short)Math.Min(copies, Int16.MaxValue);
        Marshal.StructureToPtr(mode, deviceMode, false);
    }

    private static int CheckGdiplus(int status, string queueName)
    {
        if (status != GdiplusOk)
        {
            throw new InvalidOperationException(
                $"Printing '{queueName}' failed with GDI+ status {status.ToString(CultureInfo.InvariantCulture)}.");
        }

        return status;
    }

    private void ThrowLastError(string operation) =>
        throw new InvalidOperationException(
            $"{operation} failed with Win32 error {_gdi.GetLastError()}: {Marshal.GetPInvokeErrorMessage(_gdi.GetLastError())}");
}

// What one GDI job needs beyond its pages. The values travel together from the
// driver to the page draw, so one record keeps every signature short.
internal sealed record WindowsGdiJob(
    string QueueName,

    // The suffix of the temporary file GDI+ loads, empty when the media type names none.
    // GDI+ decodes by header, so the suffix decides nothing.
    string Extension,
    string JobName,
    nint DeviceMode,
    int Copies,
    PrintOrientation? Orientation,
    PrintScaling? Scaling,

    // The resolution every page was rendered at, for a job whose pages a converter
    // made. Null for a file the caller sent, whose own resolution GDI+ reads instead.
    int? SourceDpi = null,

    // Where the page lands on the sheet. The offsets are physical, so they are turned into
    // pixels against the resolution the device reports and not the one the page was made at.
    PrintPlacement? Placement = null,

    // Whether GDI+ smooths the page as it scales it. Null is the GDI+ default, which smooths.
    bool? Smoothing = null,

    // Which rectangle of the sheet the page is fitted into: what the printer can mark, or all
    // of it.
    PrintFitArea FitArea = PrintFitArea.Printable);

// The printable area of one device page: its size in device pixels, and the dots an
// inch those pixels stand for. Both are read once a job, because a device mode does
// not change between the pages of one document. The sheet and the offset of the printable
// area inside it come along for a job fitted to the whole sheet.
internal readonly record struct PrinterPage(
    int Width,
    int Height,
    int DpiX,
    int DpiY,
    bool Borderless,
    int SheetWidth = 0,
    int SheetHeight = 0,
    int OffsetX = 0,
    int OffsetY = 0)
{
    // In the coordinates the device context draws in, which start at the printable corner. A
    // driver that reports no sheet leaves the printable area as the only rectangle there is.
    internal ImageRectangle AreaFor(PrintFitArea area) =>
        area == PrintFitArea.Physical && SheetWidth > 0 && SheetHeight > 0
            ? new ImageRectangle(-OffsetX, -OffsetY, SheetWidth, SheetHeight)
            : new ImageRectangle(0, 0, Width, Height);
}
