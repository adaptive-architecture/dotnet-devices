#nullable enable
using System.Collections.Generic;
using System.IO;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// GDI and GDI+, as far as the image print path uses them.
/// </summary>
/// <remarks>
/// It records the order of the calls, because that order is the contract: a page that was
/// started has to be ended, a document that failed has to be aborted, and the graphics, the
/// image and the device context have to be released even when the page threw. It also reads
/// the temporary file back before answering, which is how the file handling is checked
/// without a real decoder.
/// </remarks>
internal sealed class FakeWindowsGdiInterop : IWindowsGdiInterop
{
    private nint _nextHandle = 500;

    public List<string> Calls { get; } = [];

    public List<string> LoadedPaths { get; } = [];

    /// <summary>What each loaded file held, read before GDI+ would have decoded it.</summary>
    public List<byte[]> LoadedBytes { get; } = [];

    public List<(int X, int Y, int Width, int Height)> Drawn { get; } = [];

    public List<float> Rotations { get; } = [];

    public int StartupCount { get; private set; }

    public int ShutdownCount { get; private set; }

    public int OpenDeviceContexts { get; private set; }

    public int OpenGraphics { get; private set; }

    public int OpenImages { get; private set; }

    public string? DeviceName { get; private set; }

    public int JobId { get; set; } = 91;

    // The device the fake reports. HorzRes and VertRes are the printable area, the
    // Physical* pair the whole sheet, and LogPixels* the dots an inch.
    public int PrintableWidth { get; set; } = 2480;

    public int PrintableHeight { get; set; } = 3508;

    public int SheetWidth { get; set; } = 2480;

    public int SheetHeight { get; set; } = 3508;

    public int DpiX { get; set; } = 300;

    public int DpiY { get; set; } = 300;

    public uint ImageWidth { get; set; } = 1240;

    public uint ImageHeight { get; set; } = 1754;

    public float ImageDpiX { get; set; } = 150f;

    public float ImageDpiY { get; set; } = 150f;

    public string? FailingCall { get; set; }

    public int FailureError { get; set; }

    /// <summary>A GDI+ status other than Ok for the named call.</summary>
    public int GdiplusFailure { get; set; } = 1;

    public int GetLastError() => FailureError;

    public nint CreateDC(string? driver, string device, string? port, nint deviceMode)
    {
        Calls.Add(nameof(CreateDC));
        DeviceName = device;
        if (Fails(nameof(CreateDC)))
        {
            return 0;
        }

        OpenDeviceContexts++;
        return _nextHandle++;
    }

    public bool DeleteDC(nint deviceContext)
    {
        Calls.Add(nameof(DeleteDC));
        OpenDeviceContexts--;
        return true;
    }

    public int StartDoc(nint deviceContext, WindowsGdiInterop.DocInfo docInfo)
    {
        Calls.Add(nameof(StartDoc));
        return Fails(nameof(StartDoc)) ? 0 : JobId;
    }

    public int EndDoc(nint deviceContext)
    {
        Calls.Add(nameof(EndDoc));
        return Fails(nameof(EndDoc)) ? 0 : 1;
    }

    public int AbortDoc(nint deviceContext)
    {
        Calls.Add(nameof(AbortDoc));
        return 1;
    }

    public int StartPage(nint deviceContext)
    {
        Calls.Add(nameof(StartPage));
        return Fails(nameof(StartPage)) ? 0 : 1;
    }

    public int EndPage(nint deviceContext)
    {
        Calls.Add(nameof(EndPage));
        return Fails(nameof(EndPage)) ? 0 : 1;
    }

    public int GetDeviceCaps(nint deviceContext, int capability)
    {
        Calls.Add(nameof(GetDeviceCaps));
        return capability switch
        {
            WindowsGdiInterop.HorzRes => PrintableWidth,
            WindowsGdiInterop.VertRes => PrintableHeight,
            WindowsGdiInterop.PhysicalWidth => SheetWidth,
            WindowsGdiInterop.PhysicalHeight => SheetHeight,
            WindowsGdiInterop.LogPixelsX => DpiX,
            WindowsGdiInterop.LogPixelsY => DpiY,
            _ => 0,
        };
    }

    public int Startup(out nint token, WindowsGdiInterop.StartupInput input, out WindowsGdiInterop.StartupOutput output)
    {
        Calls.Add(nameof(Startup));
        output = default;
        token = 1;
        if (Fails(nameof(Startup)))
        {
            return GdiplusFailure;
        }

        StartupCount++;
        return 0;
    }

    public void Shutdown(nint token)
    {
        Calls.Add(nameof(Shutdown));
        ShutdownCount++;
    }

    public int LoadImageFromFile(string filename, out nint image)
    {
        Calls.Add(nameof(LoadImageFromFile));
        LoadedPaths.Add(filename);

        // Read it here: after this call the printer deletes the file, so this is the only
        // moment a test can see that the bytes really reached the disk.
        LoadedBytes.Add(File.Exists(filename) ? File.ReadAllBytes(filename) : []);

        if (Fails(nameof(LoadImageFromFile)))
        {
            image = 0;
            return GdiplusFailure;
        }

        OpenImages++;
        image = _nextHandle++;
        return 0;
    }

    public int DisposeImage(nint image)
    {
        Calls.Add(nameof(DisposeImage));
        OpenImages--;
        return 0;
    }

    public int GetImageWidth(nint image, out uint width)
    {
        Calls.Add(nameof(GetImageWidth));
        width = ImageWidth;
        return 0;
    }

    public int GetImageHeight(nint image, out uint height)
    {
        Calls.Add(nameof(GetImageHeight));
        height = ImageHeight;
        return 0;
    }

    public int GetImageHorizontalResolution(nint image, out float resolution)
    {
        Calls.Add(nameof(GetImageHorizontalResolution));
        resolution = ImageDpiX;
        return 0;
    }

    public int GetImageVerticalResolution(nint image, out float resolution)
    {
        Calls.Add(nameof(GetImageVerticalResolution));
        resolution = ImageDpiY;
        return 0;
    }

    public int CreateGraphics(nint deviceContext, out nint graphics)
    {
        Calls.Add(nameof(CreateGraphics));
        if (Fails(nameof(CreateGraphics)))
        {
            graphics = 0;
            return GdiplusFailure;
        }

        OpenGraphics++;
        graphics = _nextHandle++;
        return 0;
    }

    public int SetPageUnit(nint graphics, int unit)
    {
        Calls.Add(nameof(SetPageUnit));
        PageUnit = unit;
        return Fails(nameof(SetPageUnit)) ? GdiplusFailure : 0;
    }

    public int? PageUnit { get; private set; }

    public int SetInterpolationMode(nint graphics, int mode)
    {
        Calls.Add(nameof(SetInterpolationMode));
        InterpolationMode = mode;
        return Fails(nameof(SetInterpolationMode)) ? GdiplusFailure : 0;
    }

    public int? InterpolationMode { get; private set; }

    public int SetPixelOffsetMode(nint graphics, int mode)
    {
        Calls.Add(nameof(SetPixelOffsetMode));
        PixelOffsetMode = mode;
        return Fails(nameof(SetPixelOffsetMode)) ? GdiplusFailure : 0;
    }

    public int? PixelOffsetMode { get; private set; }

    public int DeleteGraphics(nint graphics)
    {
        Calls.Add(nameof(DeleteGraphics));
        OpenGraphics--;
        return 0;
    }

    public int DrawImageRect(nint graphics, nint image, int x, int y, int width, int height)
    {
        Calls.Add(nameof(DrawImageRect));
        Drawn.Add((x, y, width, height));
        return Fails(nameof(DrawImageRect)) ? GdiplusFailure : 0;
    }

    public int TranslateWorldTransform(nint graphics, float dx, float dy, int order)
    {
        Calls.Add(nameof(TranslateWorldTransform));
        return 0;
    }

    public int RotateWorldTransform(nint graphics, float angle, int order)
    {
        Calls.Add(nameof(RotateWorldTransform));
        Rotations.Add(angle);
        return 0;
    }

    public int ResetWorldTransform(nint graphics)
    {
        Calls.Add(nameof(ResetWorldTransform));
        return 0;
    }

    private bool Fails(string call) => String.Equals(FailingCall, call, StringComparison.Ordinal);
}
