using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Only the GDI and GDI+ entry points the image print path calls are declared.
// Images are decoded by GDI+ from a temporary file, so no image codec and no
// extra NuGet package is needed. All handles are closed by the caller.
//
// The platform attribute sits on each imported method and not on the type, so the constants
// and the structures stay readable on any platform. Only a call needs Windows.
internal static partial class WindowsGdiInterop
{
    // gdi32: a printer device context. The driver name stays null to take the
    // default display driver entry, and the port and init data stay null because
    // the device mode travels on the handle instead.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateDC(string? driver, string device, string? port, nint deviceMode);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint deviceContext);

    // gdi32 document bracketing. A null output file spools to the printer.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW", SetLastError = true)]
    internal static partial int StartDoc(nint deviceContext, in DocInfo docInfo);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "EndDoc", SetLastError = true)]
    internal static partial int EndDoc(nint deviceContext);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "AbortDoc", SetLastError = true)]
    internal static partial int AbortDoc(nint deviceContext);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "StartPage", SetLastError = true)]
    internal static partial int StartPage(nint deviceContext);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "EndPage", SetLastError = true)]
    internal static partial int EndPage(nint deviceContext);

    // gdi32 capabilities. HORZRES and VERTRES are the printable area in pixels.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdi32.dll", EntryPoint = "GetDeviceCaps", SetLastError = true)]
    internal static partial int GetDeviceCaps(nint deviceContext, int capability);

    internal const int HorzRes = 8;
    internal const int VertRes = 10;

    // PHYSICALWIDTH and PHYSICALHEIGHT: the whole sheet in pixels. Equal to the
    // printable area only on a borderless medium, which is what Auto asks about.
    internal const int PhysicalWidth = 110;
    internal const int PhysicalHeight = 111;

    // LOGPIXELSX and LOGPIXELSY: the dots an inch of the device holds. A job that
    // asks for its own size needs them, because a device pixel is not a length.
    internal const int LogPixelsX = 88;
    internal const int LogPixelsY = 90;

    // gdiplus: process-wide startup and shutdown around every image job.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdiplusStartup")]
    internal static partial int Startup(out nint token, in StartupInput input, out StartupOutput output);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdiplusShutdown")]
    internal static partial void Shutdown(nint token);

    // gdiplus flat drawing. The image is loaded from a file so no COM IStream
    // is needed, and drawn through a graphics object bound to the printer DC.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipLoadImageFromFile", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int LoadImageFromFile(string filename, out nint image);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipDisposeImage")]
    internal static partial int DisposeImage(nint image);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipGetImageWidth")]
    internal static partial int GetImageWidth(nint image, out uint width);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipGetImageHeight")]
    internal static partial int GetImageHeight(nint image, out uint height);

    // The dots an inch the file itself declares: the PNG pHYs chunk, the JPEG JFIF
    // density, or an EXIF tag. GDI+ answers 96 for a file that declares nothing,
    // which it then treats as that file's own size.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipGetImageHorizontalResolution")]
    internal static partial int GetImageHorizontalResolution(nint image, out float resolution);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipGetImageVerticalResolution")]
    internal static partial int GetImageVerticalResolution(nint image, out float resolution);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipCreateFromHDC")]
    internal static partial int CreateGraphics(nint deviceContext, out nint graphics);

    // A graphics object from a printer device context starts in UnitDisplay, which is
    // 1/100 inch on printers. The layout math works in device pixels from
    // GetDeviceCaps, so the unit must be switched or every rectangle renders DPI/100
    // times too large and overflows the page.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipSetPageUnit")]
    internal static partial int SetPageUnit(nint graphics, int unit);

    // GpUnitPixel: one unit is one device pixel.
    internal const int UnitPixel = 2;

    // How GDI+ reads the source when a page is drawn at a size it was not rendered at. The
    // default mixes neighboring pixels, which turns the edge of a bar into a grey ramp; a
    // job that asked for no smoothing takes the nearest pixel instead and keeps it hard.
    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipSetInterpolationMode")]
    internal static partial int SetInterpolationMode(nint graphics, int mode);

    // InterpolationModeNearestNeighbor and InterpolationModeHighQualityBicubic, from
    // GdiPlusEnums.h.
    internal const int InterpolationNearestNeighbor = 5;
    internal const int InterpolationHighQualityBicubic = 7;

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipSetPixelOffsetMode")]
    internal static partial int SetPixelOffsetMode(nint graphics, int mode);

    // PixelOffsetModeHalf: a pixel is sampled at its centre, which is where the nearest
    // neighbor above is measured from. Without it a nearest-neighbor draw lands half a
    // pixel off and the bars shift by one dot at some scales.
    internal const int PixelOffsetHalf = 4;

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipDeleteGraphics")]
    internal static partial int DeleteGraphics(nint graphics);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipDrawImageRectI")]
    internal static partial int DrawImageRect(nint graphics, nint image, int x, int y, int width, int height);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipTranslateWorldTransform")]
    internal static partial int TranslateWorldTransform(nint graphics, float dx, float dy, int order);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipRotateWorldTransform")]
    internal static partial int RotateWorldTransform(nint graphics, float angle, int order);

    [SupportedOSPlatform("windows")]
    [LibraryImport("gdiplus.dll", EntryPoint = "GdipResetWorldTransform")]
    internal static partial int ResetWorldTransform(nint graphics);

    // MatrixOrderPrepend: the transform is applied before the ones already set.
    internal const int MatrixOrderPrepend = 0;

    // wingdi.h DOCINFOW. The caller builds and frees the name pointer, like DOC_INFO_1
    // in the spooler interop: source-generated P/Invokes cannot marshal string
    // fields inside a struct.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DocInfo
    {
        internal int Size;
        internal nint DocName;
        internal nint OutputFile;
        internal nint Datatype;
        internal int Type;
    }

    // gdiplus GdiplusStartupInput. Only the version is set; the debug callback
    // stays null and nothing is suppressed.
    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupInput
    {
        internal uint Version;
        internal nint DebugEventCallback;
        internal int SuppressBackgroundThread;
        internal int SuppressExternalCodecs;

        internal static StartupInput Version1() =>
            new()
            {
                Version = 1,
            };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct StartupOutput
    {
        internal nint NotificationHook;
        internal nint NotificationUnhook;
    }
}
