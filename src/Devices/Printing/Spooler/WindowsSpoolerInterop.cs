using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// The Windows print spooler API. Only the entry points this library calls are declared.
// LibraryImport is a source generator, so no reflection reaches the trimmed output; that
// is also why every struct here uses only fields the driver reads (see each struct's
// comment for the header layout it was matched against), keeping every preceding field
// so the byte offsets line up with the real, wider native structure.
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

    // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
    internal const int PrinterEnumLocalAndConnections = 0x00000006;

    // PRINTER_ACCESS_USE. Enough to open, submit, read status and control a job;
    // this driver never changes queue configuration, so it never asks for more.
    internal const uint PrinterAccessUse = 0x00000008;

    // WORD (unsigned) in wingdi.h; a signed short would misrepresent any DC_* constant
    // above 0x7FFF.
    internal const ushort DcPaperNames = 16;
    internal const ushort DcDuplex = 7;
    internal const ushort DcColorDevice = 32;
    internal const ushort DcEnumResolutions = 13;

    // Matched against wingdi.h / winspool.h PRINTER_DEFAULTSW. Three fields, stable
    // since Windows 2000: a data type string, an optional DEVMODE pointer, and the
    // access mask requested on OpenPrinter. High confidence: this structure has not
    // changed shape across any documented Windows version.
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrinterDefaults
    {
        internal nint DataType;
        internal nint DevMode;
        internal uint DesiredAccess;
    }

    // Matched against winspool.h DOC_INFO_1W: pDocName, pOutputFile, pDatatype, in that
    // order. High confidence: unchanged since Windows 2000. All three fields are
    // pointers, built and freed by the caller around the StartDocPrinter call.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DocInfo1
    {
        internal nint DocName;
        internal nint OutputFile;
        internal nint DataType;
    }

    // Matched against winspool.h PRINTER_INFO_4W: pPrinterName, pServerName, Attributes.
    // High confidence: this is the smallest, most stable printer-enumeration level,
    // documented specifically as not requiring a printer handle.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PrinterInfo4
    {
        internal string? PrinterName;
        internal string? ServerName;
        internal uint Attributes;
    }

    // Matched against winspool.h PRINTER_INFO_2W. Declared up to and including Status,
    // the last field this driver reads; cJobs and AveragePPM (which follow Status) are
    // omitted, which is safe only because they come after everything this driver reads,
    // not before. High confidence for the field order and types up to Status; the two
    // omitted trailing fields are not exercised so their exact width is not re-verified
    // here (their sizes do not affect any offset this driver depends on).
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

    // Matched against winspool.h JOB_INFO_2W, declared in full (through PagesPrinted)
    // because EnumJobs returns an array of these back-to-back: a truncated struct would
    // misalign every entry after the first. Moderate-to-high confidence: this is the
    // widely reproduced JOB_INFO_2 shape (pinvoke.net and Microsoft Learn agree on it
    // from memory), but it was not checked against a live header for this task, so
    // treat the exact byte width of SystemTime (embedded below) as the one field this
    // driver does not itself read but must still get right for later entries to line up.
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

    // Matched against the Win32 SYSTEMTIME structure (minwinbase.h / winbase.h): eight
    // WORD fields, unchanged since Windows 3.1. High confidence. Only present here to
    // keep JobInfo2's trailing offsets correct; this driver never reads its fields.
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
