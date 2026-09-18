using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Prints PNG or JPEG pages through the Windows printer driver with GDI,
// so the driver rasterises every page. The RAW spooler path passes bytes
// unchanged and cannot do this; a printer whose firmware reads only its own
// page language prints nothing for an image sent that way. One job carries
// every page, so a PDF renders once and prints as one document.
[SupportedOSPlatform("windows")]
internal static class WindowsGdiImagePrinter
{
    // wingdi.h DM_COPIES. The GDI path honours the copy count in the device
    // mode, so one job prints every copy. The RAW path loops instead, because
    // a RAW queue never reads this field.
    private const uint DmCopies = 0x00000100;

    // GDI+ status Ok.
    private const int GdiplusOk = 0;

    internal static int Print(WindowsGdiJob job, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return PrintPages(job, [bytes]);
    }

    internal static int PrintPages(WindowsGdiJob job, IReadOnlyList<byte[]> pages)
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

    private static int PrintFile(WindowsGdiJob job, IReadOnlyList<byte[]> pages)
    {
        var queueName = job.QueueName;
        ApplyCopies(job.DeviceMode, job.Copies);

        var input = WindowsGdiInterop.StartupInput.Version1();
        var startupStatus = WindowsGdiInterop.Startup(out var token, in input, out _);
        if (startupStatus != GdiplusOk)
        {
            throw new InvalidOperationException(
                $"Printing '{queueName}' failed to start GDI+ with status {startupStatus}.");
        }

        var deviceContext = IntPtr.Zero;
        var documentStarted = false;
        try
        {
            deviceContext = WindowsGdiInterop.CreateDC(null, queueName, null, job.DeviceMode);
            if (deviceContext == IntPtr.Zero)
            {
                ThrowLastError("CreateDC");
            }

            var printableWidth = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.HorzRes);
            var printableHeight = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.VertRes);
            var sheetWidth = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalWidth);
            var sheetHeight = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.PhysicalHeight);
            var page = new PrinterPage(
                printableWidth,
                printableHeight,
                WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.LogPixelsX),
                WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.LogPixelsY),
                // A driver that reports no sheet keeps its margins, which is the answer
                // every printer but a borderless one gives anyway.
                sheetWidth > 0 && sheetHeight > 0 && sheetWidth <= printableWidth && sheetHeight <= printableHeight);
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
                var jobId = WindowsGdiInterop.StartDoc(deviceContext, in docInfo);
                if (jobId <= 0)
                {
                    ThrowLastError("StartDoc");
                }

                documentStarted = true;
                foreach (var pageBytes in pages)
                {
                    DrawPage(deviceContext, job, pageBytes, page);
                }

                if (WindowsGdiInterop.EndDoc(deviceContext) <= 0)
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
                _ = WindowsGdiInterop.AbortDoc(deviceContext);
            }

            if (deviceContext != IntPtr.Zero)
            {
                _ = WindowsGdiInterop.DeleteDC(deviceContext);
            }

            WindowsGdiInterop.Shutdown(token);
        }
    }

    // One page of the job: decode, lay out, draw, spool. A failure here leaves the
    // page open, so the caller aborts the whole document and no truncated page commits.
    private static void DrawPage(
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
            var loadStatus = WindowsGdiInterop.LoadImageFromFile(temporaryPath, out image);
            if (loadStatus != GdiplusOk)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' could not decode the image with GDI+ status {loadStatus}.");
            }

            _ = CheckGdiplus(WindowsGdiInterop.GetImageWidth(image, out var width), queueName);
            _ = CheckGdiplus(WindowsGdiInterop.GetImageHeight(image, out var height), queueName);
            _ = CheckGdiplus(WindowsGdiInterop.GetImageHorizontalResolution(image, out var sourceDpiX), queueName);
            _ = CheckGdiplus(WindowsGdiInterop.GetImageVerticalResolution(image, out var sourceDpiY), queueName);

            // A converted page carries the resolution it was rendered at, because the
            // encoder writes none of its own and the image would claim 96. A page the
            // caller sent keeps what its own file declares.
            var sourceX = job.SourceDpi ?? sourceDpiX;
            var sourceY = job.SourceDpi ?? sourceDpiY;
            var layout = WindowsGdiImageLayout.Compute(
                WindowsGdiImageLayout.NaturalPixels((int)width, sourceX, page.DpiX),
                WindowsGdiImageLayout.NaturalPixels((int)height, sourceY, page.DpiY),
                page.Width,
                page.Height,
                job.Orientation,
                job.Scaling,
                page.Borderless);
            if (layout.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' found an empty image of {width}x{height} pixels.");
            }

            if (WindowsGdiInterop.StartPage(deviceContext) <= 0)
            {
                ThrowLastError("StartPage");
            }

            pageStarted = true;
            _ = CheckGdiplus(WindowsGdiInterop.CreateGraphics(deviceContext, out graphics), queueName);
            _ = CheckGdiplus(WindowsGdiInterop.SetPageUnit(graphics, WindowsGdiInterop.UnitPixel), queueName);

            var angle = WindowsGdiImageLayout.RotationDegrees(job.Orientation);
            if (angle != 0f)
            {
                _ = CheckGdiplus(
                    WindowsGdiInterop.TranslateWorldTransform(graphics, page.Width / 2f, page.Height / 2f, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    WindowsGdiInterop.RotateWorldTransform(graphics, angle, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    WindowsGdiInterop.TranslateWorldTransform(graphics, -page.Width / 2f, -page.Height / 2f, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
            }

            _ = CheckGdiplus(
                WindowsGdiInterop.DrawImageRect(graphics, image, layout.X, layout.Y, layout.Width, layout.Height),
                queueName);

            _ = CheckGdiplus(WindowsGdiInterop.DeleteGraphics(graphics), queueName);
            graphics = IntPtr.Zero;

            if (WindowsGdiInterop.EndPage(deviceContext) <= 0)
            {
                ThrowLastError("EndPage");
            }

            pageStarted = false;
        }
        finally
        {
            if (graphics != IntPtr.Zero)
            {
                _ = WindowsGdiInterop.DeleteGraphics(graphics);
            }

            if (pageStarted)
            {
                _ = WindowsGdiInterop.EndPage(deviceContext);
            }

            if (image != IntPtr.Zero)
            {
                _ = WindowsGdiInterop.DisposeImage(image);
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

    private static void ThrowLastError(string operation) =>
        throw new InvalidOperationException(
            $"{operation} failed with Win32 error {Marshal.GetLastWin32Error()}: {Marshal.GetPInvokeErrorMessage(Marshal.GetLastWin32Error())}");
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
    int? SourceDpi = null);

// The printable area of one device page: its size in device pixels, and the dots an
// inch those pixels stand for. Both are read once a job, because a device mode does
// not change between the pages of one document.
internal readonly record struct PrinterPage(int Width, int Height, int DpiX, int DpiY, bool Borderless);
