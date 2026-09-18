using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

/// <summary>
/// The seam in front of <c>winspool.drv</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point the driver calls is here, so a test on any operating system can answer
/// them: the two-call buffer protocol, the structures the spooler writes into those buffers,
/// and the Win32 error codes are all logic this library owns, and none of it needs Windows
/// to be exercised.
/// </para>
/// <para>
/// The constants and the structures stay on <see cref="WindowsSpoolerInterop"/>. They are
/// declarations, not calls, and <see cref="System.Runtime.InteropServices.Marshal"/> reads
/// them correctly on every platform.
/// </para>
/// <para>
/// Every member mirrors its <c>winspool.drv</c> entry point parameter for parameter. That
/// is what keeps the adapter a single forwarding call, and the adapter is the one piece
/// here that no test can reach: a parameter object would make it code that could be wrong.
/// </para>
/// </remarks>
internal interface IWindowsSpoolerInterop
{
    bool OpenPrinter(string printerName, out nint printerHandle, WindowsSpoolerInterop.PrinterDefaults defaults);

    bool ClosePrinter(nint printerHandle);

    int StartDocPrinter(nint printerHandle, int level, WindowsSpoolerInterop.DocInfo1 documentInfo);

    bool EndDocPrinter(nint printerHandle);

    bool StartPagePrinter(nint printerHandle);

    bool EndPagePrinter(nint printerHandle);

    bool WritePrinter(nint printerHandle, nint buffer, int count, out int written);

    bool EnumPrinters(int flags, nint name, int level, nint buffer, int bufferSize, out int needed, out int returned);

    // EnumJobsW takes eight parameters and this mirrors it. Folding them into a parameter
    // object would move the marshalling into a type and leave the adapter with something to
    // get wrong, which is the opposite of why this interface exists.
#pragma warning disable S107 // Methods should not have too many parameters
    bool EnumJobs(nint printerHandle, int firstJob, int jobCount, int level, nint buffer, int bufferSize, out int needed, out int returned);
#pragma warning restore S107

    bool SetJob(nint printerHandle, int jobId, int level, nint job, int command);

    bool GetPrinter(nint printerHandle, int level, nint buffer, int bufferSize, out int needed);

    int DeviceCapabilities(string device, string? port, ushort capability, nint output, nint deviceMode);

    int DocumentProperties(nint window, nint printerHandle, string deviceName, nint output, nint input, uint mode);

    /// <summary>
    /// The error code of the last call. A fake sets it, which is what makes the error paths
    /// of the driver reachable: no real printer produces 1801, 87 and 1722 on demand.
    /// </summary>
    int GetLastError();
}
