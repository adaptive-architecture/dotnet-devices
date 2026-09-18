using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Nothing but the call. This type is the thin layer the coverage exclusion list is for.
// The attribute sits on each member, so the cross-platform driver may hold an instance and
// only a call to it needs Windows.
internal sealed class WindowsSpoolerInteropAdapter : IWindowsSpoolerInterop
{
    public static readonly WindowsSpoolerInteropAdapter Instance = new();

    [SupportedOSPlatform("windows")]
    public bool OpenPrinter(string printerName, out nint printerHandle, WindowsSpoolerInterop.PrinterDefaults defaults) =>
        WindowsSpoolerInterop.OpenPrinter(printerName, out printerHandle, in defaults);

    [SupportedOSPlatform("windows")]
    public bool ClosePrinter(nint printerHandle) => WindowsSpoolerInterop.ClosePrinter(printerHandle);

    [SupportedOSPlatform("windows")]
    public int StartDocPrinter(nint printerHandle, int level, WindowsSpoolerInterop.DocInfo1 documentInfo) =>
        WindowsSpoolerInterop.StartDocPrinter(printerHandle, level, in documentInfo);

    [SupportedOSPlatform("windows")]
    public bool EndDocPrinter(nint printerHandle) => WindowsSpoolerInterop.EndDocPrinter(printerHandle);

    [SupportedOSPlatform("windows")]
    public bool StartPagePrinter(nint printerHandle) => WindowsSpoolerInterop.StartPagePrinter(printerHandle);

    [SupportedOSPlatform("windows")]
    public bool EndPagePrinter(nint printerHandle) => WindowsSpoolerInterop.EndPagePrinter(printerHandle);

    [SupportedOSPlatform("windows")]
    public bool WritePrinter(nint printerHandle, nint buffer, int count, out int written) =>
        WindowsSpoolerInterop.WritePrinter(printerHandle, buffer, count, out written);

    [SupportedOSPlatform("windows")]
    public bool EnumPrinters(int flags, nint name, int level, nint buffer, int bufferSize, out int needed, out int returned) =>
        WindowsSpoolerInterop.EnumPrinters(flags, name, level, buffer, bufferSize, out needed, out returned);

    [SupportedOSPlatform("windows")]
    public bool EnumJobs(nint printerHandle, int firstJob, int jobCount, int level, nint buffer, int bufferSize, out int needed, out int returned) =>
        WindowsSpoolerInterop.EnumJobs(printerHandle, firstJob, jobCount, level, buffer, bufferSize, out needed, out returned);

    [SupportedOSPlatform("windows")]
    public bool SetJob(nint printerHandle, int jobId, int level, nint job, int command) =>
        WindowsSpoolerInterop.SetJob(printerHandle, jobId, level, job, command);

    [SupportedOSPlatform("windows")]
    public bool GetPrinter(nint printerHandle, int level, nint buffer, int bufferSize, out int needed) =>
        WindowsSpoolerInterop.GetPrinter(printerHandle, level, buffer, bufferSize, out needed);

    [SupportedOSPlatform("windows")]
    public int DeviceCapabilities(string device, string? port, ushort capability, nint output, nint deviceMode) =>
        WindowsSpoolerInterop.DeviceCapabilities(device, port, capability, output, deviceMode);

    [SupportedOSPlatform("windows")]
    public int DocumentProperties(nint window, nint printerHandle, string deviceName, nint output, nint input, uint mode) =>
        WindowsSpoolerInterop.DocumentProperties(window, printerHandle, deviceName, output, input, mode);

    [SupportedOSPlatform("windows")]
    public int GetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}
