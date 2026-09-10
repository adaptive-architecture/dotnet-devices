using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Only the entry points this library calls are declared.
[SupportedOSPlatform("windows")]
internal static partial class WindowsSpoolerInterop
{
    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenPrinter(string printerName, out nint printerHandle, in PrinterDefaults defaults);

    [LibraryImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClosePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true)]
    internal static partial int StartDocPrinter(nint printerHandle, int level, in DocInfo1 documentInfo);

    [LibraryImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndDocPrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool StartPagePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPagePrinter(nint printerHandle);

    [LibraryImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WritePrinter(nint printerHandle, nint buffer, int count, out int written);

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumPrinters(int flags, nint name, int level, nint buffer, int bufferSize, out int needed, out int returned);

    [LibraryImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumJobs(nint printerHandle, int firstJob, int jobCount, int level, nint buffer, int bufferSize, out int needed, out int returned);

    [LibraryImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetJob(nint printerHandle, int jobId, int level, nint job, int command);

    [LibraryImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPrinter(nint printerHandle, int level, nint buffer, int bufferSize, out int needed);

    [LibraryImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial int DeviceCapabilities(string device, string? port, ushort capability, nint output, nint deviceMode);

    // JOB_CONTROL_CANCEL
    internal const int JobControlCancel = 3;

    // JOB_CONTROL_DELETE: removes a job that was started but not written in full.
    internal const int JobControlDelete = 5;

    // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
    internal const int PrinterEnumLocalAndConnections = 0x00000006;

    // PRINTER_ACCESS_USE: enough to open, submit, read status and control a job.
    internal const uint PrinterAccessUse = 0x00000008;

    // WORD (unsigned) in wingdi.h: a signed short would misrepresent DC_* above 0x7FFF.
    internal const ushort DcPaperNames = 16;
    internal const ushort DcDuplex = 7;
    internal const ushort DcColorDevice = 32;
    internal const ushort DcEnumResolutions = 13;

    // winspool.h PRINTER_DEFAULTSW.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrinterDefaults
    {
        internal nint DataType;
        internal nint DevMode;
        internal uint DesiredAccess;
    }

    // winspool.h DOC_INFO_1W. The caller builds and frees all three pointers.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DocInfo1
    {
        internal nint DocName;
        internal nint OutputFile;
        internal nint DataType;
    }

    // winspool.h PRINTER_INFO_4W.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrinterInfo4
    {
        internal string? PrinterName;
        internal string? ServerName;
        internal uint Attributes;
    }

    // winspool.h PRINTER_INFO_2W, declared up to Status, the last field read here.
    // The trailing cJobs and AveragePPM are omitted: they affect no offset used.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrinterInfo2
    {
        internal string? ServerName;
        internal string? PrinterName;
        internal string? ShareName;
        internal string? PortName;
        internal string? DriverName;
        internal string? Comment;
        internal string? Location;
        internal nint DevMode;
        internal string? SepFile;
        internal string? PrintProcessor;
        internal string? Datatype;
        internal string? Parameters;
        internal nint SecurityDescriptor;
        internal uint Attributes;
        internal uint Priority;
        internal uint DefaultPriority;
        internal uint StartTime;
        internal uint UntilTime;
        internal uint Status;
    }

    // winspool.h JOB_INFO_2W, declared in full: EnumJobs returns an array of these
    // back-to-back, so a truncated struct would misalign every entry after the first.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JobInfo2
    {
        internal uint JobId;
        internal string? PrinterName;
        internal string? MachineName;
        internal string? UserName;
        internal string? Document;
        internal string? NotifyName;
        internal string? Datatype;
        internal string? PrintProcessor;
        internal string? Parameters;
        internal string? DriverName;
        internal nint DevMode;
        internal string? StatusMessage;
        internal nint SecurityDescriptor;
        internal uint Status;
        internal uint Priority;
        internal uint Position;
        internal uint StartTime;
        internal uint UntilTime;
        internal uint TotalPages;
        internal uint Size;
        internal SystemTime Submitted;
        internal uint Time;
        internal uint PagesPrinted;
    }

    // SYSTEMTIME. Present only to keep the JobInfo2 offsets after it correct.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }
}
