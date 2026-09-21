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

    int SetInterpolationMode(nint graphics, int mode);

    int SetPixelOffsetMode(nint graphics, int mode);

    int DeleteGraphics(nint graphics);

    int DrawImageRect(nint graphics, nint image, int x, int y, int width, int height);

    int TranslateWorldTransform(nint graphics, float dx, float dy, int order);

    int RotateWorldTransform(nint graphics, float angle, int order);

    int ResetWorldTransform(nint graphics);

    /// <summary>The error code of the last call, for the paths that report one.</summary>
    int GetLastError();
}
