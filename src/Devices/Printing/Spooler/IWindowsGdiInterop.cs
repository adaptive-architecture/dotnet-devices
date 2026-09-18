using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// The seam in front of <c>gdi32.dll</c> and <c>gdiplus.dll</c>.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="IWindowsSpoolerInterop"/>, for the same reason: what
/// is worth testing in the image path is the page loop, the resolution arithmetic, the
/// temporary file and the order in which a failed page is torn down, and none of that is
/// native code. A fake answers these calls and records the drawing that resulted.
/// </remarks>
internal interface IWindowsGdiInterop
{
    nint CreateDC(string? driver, string device, string? port, nint deviceMode);

    bool DeleteDC(nint deviceContext);

    int StartDoc(nint deviceContext, WindowsGdiInterop.DocInfo docInfo);

    int EndDoc(nint deviceContext);

    int AbortDoc(nint deviceContext);

    int StartPage(nint deviceContext);

    int EndPage(nint deviceContext);

    int GetDeviceCaps(nint deviceContext, int capability);

    int Startup(out nint token, WindowsGdiInterop.StartupInput input, out WindowsGdiInterop.StartupOutput output);

    void Shutdown(nint token);

    int LoadImageFromFile(string filename, out nint image);

    int DisposeImage(nint image);

    int GetImageWidth(nint image, out uint width);

    int GetImageHeight(nint image, out uint height);

    int GetImageHorizontalResolution(nint image, out float resolution);

    int GetImageVerticalResolution(nint image, out float resolution);

    int CreateGraphics(nint deviceContext, out nint graphics);

    int SetPageUnit(nint graphics, int unit);

    int DeleteGraphics(nint graphics);

    int DrawImageRect(nint graphics, nint image, int x, int y, int width, int height);

    int TranslateWorldTransform(nint graphics, float dx, float dy, int order);

    int RotateWorldTransform(nint graphics, float angle, int order);

    int ResetWorldTransform(nint graphics);

    /// <summary>The error code of the last call, for the paths that report one.</summary>
    int GetLastError();
}

// Nothing but the call. This type is the thin layer the coverage exclusion list is for.
internal sealed class WindowsGdiInteropAdapter : IWindowsGdiInterop
{
    public static readonly WindowsGdiInteropAdapter Instance = new();

    [SupportedOSPlatform("windows")]
    public nint CreateDC(string? driver, string device, string? port, nint deviceMode) =>
        WindowsGdiInterop.CreateDC(driver, device, port, deviceMode);

    [SupportedOSPlatform("windows")]
    public bool DeleteDC(nint deviceContext) =>
        WindowsGdiInterop.DeleteDC(deviceContext);

    [SupportedOSPlatform("windows")]
    public int StartDoc(nint deviceContext, WindowsGdiInterop.DocInfo docInfo) =>
        WindowsGdiInterop.StartDoc(deviceContext, docInfo);

    [SupportedOSPlatform("windows")]
    public int EndDoc(nint deviceContext) =>
        WindowsGdiInterop.EndDoc(deviceContext);

    [SupportedOSPlatform("windows")]
    public int AbortDoc(nint deviceContext) =>
        WindowsGdiInterop.AbortDoc(deviceContext);

    [SupportedOSPlatform("windows")]
    public int StartPage(nint deviceContext) =>
        WindowsGdiInterop.StartPage(deviceContext);

    [SupportedOSPlatform("windows")]
    public int EndPage(nint deviceContext) =>
        WindowsGdiInterop.EndPage(deviceContext);

    [SupportedOSPlatform("windows")]
    public int GetDeviceCaps(nint deviceContext, int capability) =>
        WindowsGdiInterop.GetDeviceCaps(deviceContext, capability);

    [SupportedOSPlatform("windows")]
    public int Startup(out nint token, WindowsGdiInterop.StartupInput input, out WindowsGdiInterop.StartupOutput output) =>
        WindowsGdiInterop.Startup(out token, input, out output);

    [SupportedOSPlatform("windows")]
    public void Shutdown(nint token) =>
        WindowsGdiInterop.Shutdown(token);

    [SupportedOSPlatform("windows")]
    public int LoadImageFromFile(string filename, out nint image) =>
        WindowsGdiInterop.LoadImageFromFile(filename, out image);

    [SupportedOSPlatform("windows")]
    public int DisposeImage(nint image) =>
        WindowsGdiInterop.DisposeImage(image);

    [SupportedOSPlatform("windows")]
    public int GetImageWidth(nint image, out uint width) =>
        WindowsGdiInterop.GetImageWidth(image, out width);

    [SupportedOSPlatform("windows")]
    public int GetImageHeight(nint image, out uint height) =>
        WindowsGdiInterop.GetImageHeight(image, out height);

    [SupportedOSPlatform("windows")]
    public int GetImageHorizontalResolution(nint image, out float resolution) =>
        WindowsGdiInterop.GetImageHorizontalResolution(image, out resolution);

    [SupportedOSPlatform("windows")]
    public int GetImageVerticalResolution(nint image, out float resolution) =>
        WindowsGdiInterop.GetImageVerticalResolution(image, out resolution);

    [SupportedOSPlatform("windows")]
    public int CreateGraphics(nint deviceContext, out nint graphics) =>
        WindowsGdiInterop.CreateGraphics(deviceContext, out graphics);

    [SupportedOSPlatform("windows")]
    public int SetPageUnit(nint graphics, int unit) =>
        WindowsGdiInterop.SetPageUnit(graphics, unit);

    [SupportedOSPlatform("windows")]
    public int DeleteGraphics(nint graphics) =>
        WindowsGdiInterop.DeleteGraphics(graphics);

    [SupportedOSPlatform("windows")]
    public int DrawImageRect(nint graphics, nint image, int x, int y, int width, int height) =>
        WindowsGdiInterop.DrawImageRect(graphics, image, x, y, width, height);

    [SupportedOSPlatform("windows")]
    public int TranslateWorldTransform(nint graphics, float dx, float dy, int order) =>
        WindowsGdiInterop.TranslateWorldTransform(graphics, dx, dy, order);

    [SupportedOSPlatform("windows")]
    public int RotateWorldTransform(nint graphics, float angle, int order) =>
        WindowsGdiInterop.RotateWorldTransform(graphics, angle, order);

    [SupportedOSPlatform("windows")]
    public int ResetWorldTransform(nint graphics) =>
        WindowsGdiInterop.ResetWorldTransform(graphics);

    public int GetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}
