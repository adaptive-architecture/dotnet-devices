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

    internal static int Print(
        string queueName,
        byte[] bytes,
        string extension,
        string jobName,
        nint deviceMode,
        int copies,
        PrintOrientation? orientation,
        PrintScaling? scaling)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return PrintPages(queueName, [bytes], extension, jobName, deviceMode, copies, orientation, scaling);
    }

    internal static int PrintPages(
        string queueName,
        IReadOnlyList<byte[]> pages,
        string extension,
        string jobName,
        nint deviceMode,
        int copies,
        PrintOrientation? orientation,
        PrintScaling? scaling)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        if (pages.Count == 0)
        {
            throw new ArgumentException("A job prints at least one page.", nameof(pages));
        }

        return PrintFile(queueName, pages, extension, jobName, deviceMode, copies, orientation, scaling);
    }

    private static int PrintFile(
        string queueName,
        IReadOnlyList<byte[]> pages,
        string extension,
        string jobName,
        nint deviceMode,
        int copies,
        PrintOrientation? orientation,
        PrintScaling? scaling)
    {
        ApplyCopies(deviceMode, copies);

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
            deviceContext = WindowsGdiInterop.CreateDC(null, queueName, null, deviceMode);
            if (deviceContext == IntPtr.Zero)
            {
                ThrowLastError("CreateDC");
            }

            var pageWidth = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.HorzRes);
            var pageHeight = WindowsGdiInterop.GetDeviceCaps(deviceContext, WindowsGdiInterop.VertRes);
            if (pageWidth <= 0 || pageHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"Printing '{queueName}' reported an unusable page of {pageWidth}x{pageHeight} pixels.");
            }

            var docNamePtr = Marshal.StringToHGlobalUni(jobName);
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
                    DrawPage(deviceContext, pageBytes, extension, pageWidth, pageHeight, orientation, scaling, queueName);
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
        byte[] bytes,
        string extension,
        int pageWidth,
        int pageHeight,
        PrintOrientation? orientation,
        PrintScaling? scaling,
        string queueName)
    {
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
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
            var layout = WindowsGdiImageLayout.Compute((int)width, (int)height, pageWidth, pageHeight, orientation, scaling);
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

            var angle = WindowsGdiImageLayout.RotationDegrees(orientation);
            if (angle != 0f)
            {
                _ = CheckGdiplus(
                    WindowsGdiInterop.TranslateWorldTransform(graphics, pageWidth / 2f, pageHeight / 2f, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    WindowsGdiInterop.RotateWorldTransform(graphics, angle, WindowsGdiInterop.MatrixOrderPrepend),
                    queueName);
                _ = CheckGdiplus(
                    WindowsGdiInterop.TranslateWorldTransform(graphics, -pageWidth / 2f, -pageHeight / 2f, WindowsGdiInterop.MatrixOrderPrepend),
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
