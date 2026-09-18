using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

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
