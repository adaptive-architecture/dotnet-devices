#nullable enable
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AdaptArch.Devices.Printing.Spooler;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// A print spooler that answers the way <c>winspool.drv</c> does, without being it.
/// </summary>
/// <remarks>
/// It writes real <c>PRINTER_INFO_4</c>, <c>PRINTER_INFO_2</c>, <c>JOB_INFO_2</c> and
/// <c>DEVMODEW</c> bytes into the buffers the driver hands it, so the driver reads them
/// with the same <see cref="Marshal"/> calls it uses on Windows and the structure layout is
/// under test rather than assumed. It also answers the two-call buffer protocol the way the
/// spooler does — a refusal with ERROR_INSUFFICIENT_BUFFER and the size, then the data —
/// and it can fail any call with any Win32 code, which is how the error paths become
/// reachable at all: no real printer produces 1801, 87 or 1722 on request.
///
/// <c>Marshal.StructureToPtr</c> allocates a block for each string
/// field, and the driver frees only the buffer around them, exactly as it does against the
/// real spooler, which owns those strings. The blocks therefore outlive the test. It is a
/// few kilobytes for a whole run and buys a faithful layout.
/// </remarks>
internal sealed class FakeWindowsSpoolerInterop : IWindowsSpoolerInterop
{
    // ERROR_INVALID_HANDLE, for a call on a handle this spooler never opened.
    private const int ErrorInvalidHandle = 6;

    private const int ErrorInsufficientBuffer = 122;

    private readonly HashSet<nint> _openHandles = [];
    private nint _nextHandle = 1000;
    private int _nextJobId = 1;

    public List<string> Calls { get; } = [];

    public List<string> OpenedQueues { get; } = [];

    /// <summary>Everything WritePrinter received, one entry per job.</summary>
    public List<byte[]> Written { get; } = [];

    public List<int> DeletedJobs { get; } = [];

    public List<int> CancelledJobs { get; } = [];

    public List<int> StartedJobs { get; } = [];

    public int LastError { get; set; }

    public int OpenHandleCount => _openHandles.Count;

    // What the spooler reports. A test sets these before the call it is about.
    public List<string> Queues { get; } = ["lobby"];

    public WindowsSpoolerInterop.PrinterInfo2 PrinterInfo { get; set; } = new()
    {
        PrinterName = "lobby",
        PortName = "USB001",
        Comment = "Lobby LaserJet",
        Location = "Reception",
    };

    public List<WindowsSpoolerInterop.JobInfo2> Jobs { get; } = [];

    public WindowsSpoolerInterop.DevMode DeviceMode { get; set; } = new()
    {
        DeviceName = "lobby",
        FormName = "A4",
        Size = (ushort)Marshal.SizeOf<WindowsSpoolerInterop.DevMode>(),
        PaperSize = 9,
        Orientation = 1,
    };

    /// <summary>The device mode the driver asked the spooler to accept, read back.</summary>
    public WindowsSpoolerInterop.DevMode? AcceptedDeviceMode { get; private set; }

    /// <summary>Answers for DeviceCapabilities, keyed by the DC_* value.</summary>
    public Dictionary<ushort, object> Capabilities { get; } = [];

    // Failure switches. Each names the call that must fail and the code it reports.
    public string? FailingCall { get; set; }

    public int FailureError { get; set; }

    /// <summary>Bytes WritePrinter accepts per call; 0 means all of them.</summary>
    public int WriteChunk { get; set; }

    /// <summary>Makes WritePrinter report success and no progress, which must not loop.</summary>
    public bool WriteNothing { get; set; }

    /// <summary>A device mode shorter than DEVMODEW, which the driver must refuse.</summary>
    public ushort? ShortDeviceModeSize { get; set; }

    public int GetLastError() => LastError;

    public bool OpenPrinter(string printerName, out nint printerHandle, WindowsSpoolerInterop.PrinterDefaults defaults)
    {
        Calls.Add(nameof(OpenPrinter));
        OpenedQueues.Add(printerName);

        if (Fails(nameof(OpenPrinter)))
        {
            // The real OpenPrinter leaves the handle undefined, so this gives back a value
            // the driver must not keep: it has to zero it itself before the cleanup runs.
            printerHandle = 0x0BADF00D;
            return false;
        }

        printerHandle = _nextHandle++;
        _ = _openHandles.Add(printerHandle);
        return true;
    }

    public bool ClosePrinter(nint printerHandle)
    {
        Calls.Add(nameof(ClosePrinter));
        return _openHandles.Remove(printerHandle);
    }

    public int StartDocPrinter(nint printerHandle, int level, WindowsSpoolerInterop.DocInfo1 documentInfo)
    {
        Calls.Add(nameof(StartDocPrinter));
        if (!_openHandles.Contains(printerHandle))
        {
            LastError = ErrorInvalidHandle;
            return 0;
        }

        if (Fails(nameof(StartDocPrinter)))
        {
            return 0;
        }

        var jobId = _nextJobId++;
        StartedJobs.Add(jobId);
        _pending = [];
        return jobId;
    }

    public bool EndDocPrinter(nint printerHandle)
    {
        Calls.Add(nameof(EndDocPrinter));
        if (_pending is not null)
        {
            Written.Add([.. _pending]);
            _pending = null;
        }

        return true;
    }

    public bool StartPagePrinter(nint printerHandle)
    {
        Calls.Add(nameof(StartPagePrinter));
        return !Fails(nameof(StartPagePrinter));
    }

    public bool EndPagePrinter(nint printerHandle)
    {
        Calls.Add(nameof(EndPagePrinter));
        return true;
    }

    private List<byte>? _pending;

    public bool WritePrinter(nint printerHandle, nint buffer, int count, out int written)
    {
        Calls.Add(nameof(WritePrinter));

        if (Fails(nameof(WritePrinter)))
        {
            written = 0;
            return false;
        }

        if (WriteNothing)
        {
            written = 0;
            return true;
        }

        written = WriteChunk > 0 ? Math.Min(WriteChunk, count) : count;
        var bytes = new byte[written];
        Marshal.Copy(buffer, bytes, 0, written);
        (_pending ??= []).AddRange(bytes);
        return true;
    }

    public bool EnumPrinters(int flags, nint name, int level, nint buffer, int bufferSize, out int needed, out int returned)
    {
        Calls.Add(nameof(EnumPrinters));

        if (Fails(nameof(EnumPrinters)))
        {
            needed = 0;
            returned = 0;
            return false;
        }

        var itemSize = Marshal.SizeOf<WindowsSpoolerInterop.PrinterInfo4>();
        needed = itemSize * Queues.Count;
        returned = 0;

        if (buffer == 0 || bufferSize < needed)
        {
            LastError = ErrorInsufficientBuffer;
            return false;
        }

        for (var i = 0; i < Queues.Count; i++)
        {
            WindowsSpoolerInterop.PrinterInfo4 info = new() { PrinterName = Queues[i], ServerName = null, Attributes = 0 };
            Marshal.StructureToPtr(info, buffer + (i * itemSize), false);
        }

        returned = Queues.Count;
        return true;
    }

    public bool EnumJobs(nint printerHandle, int firstJob, int jobCount, int level, nint buffer, int bufferSize, out int needed, out int returned)
    {
        Calls.Add(nameof(EnumJobs));

        if (Fails(nameof(EnumJobs)))
        {
            needed = 0;
            returned = 0;
            return false;
        }

        var itemSize = Marshal.SizeOf<WindowsSpoolerInterop.JobInfo2>();
        needed = itemSize * Jobs.Count;
        returned = 0;

        if (Jobs.Count == 0)
        {
            return true;
        }

        if (buffer == 0 || bufferSize < needed)
        {
            LastError = ErrorInsufficientBuffer;
            return false;
        }

        for (var i = 0; i < Jobs.Count; i++)
        {
            Marshal.StructureToPtr(Jobs[i], buffer + (i * itemSize), false);
        }

        returned = Jobs.Count;
        return true;
    }

    public bool SetJob(nint printerHandle, int jobId, int level, nint job, int command)
    {
        Calls.Add(nameof(SetJob));

        if (command == 5)
        {
            DeletedJobs.Add(jobId);
            // A deleted job spools nothing, so what was written for it is dropped too.
            _pending = null;
            return true;
        }

        if (command == 3)
        {
            if (Fails(nameof(SetJob)))
            {
                return false;
            }

            CancelledJobs.Add(jobId);
            return true;
        }

        return true;
    }

    public bool GetPrinter(nint printerHandle, int level, nint buffer, int bufferSize, out int needed)
    {
        Calls.Add(nameof(GetPrinter));

        if (Fails(nameof(GetPrinter)))
        {
            needed = 0;
            return false;
        }

        needed = Marshal.SizeOf<WindowsSpoolerInterop.PrinterInfo2>();
        if (buffer == 0 || bufferSize < needed)
        {
            LastError = ErrorInsufficientBuffer;
            return false;
        }

        Marshal.StructureToPtr(PrinterInfo, buffer, false);
        return true;
    }

    public int DeviceCapabilities(string device, string? port, ushort capability, nint output, nint deviceMode)
    {
        Calls.Add(nameof(DeviceCapabilities));

        if (Fails(nameof(DeviceCapabilities)))
        {
            return -1;
        }

        if (!Capabilities.TryGetValue(capability, out var answer))
        {
            return 0;
        }

        return answer switch
        {
            int number => number,
            short[] words => WriteWords(words, output),
            NameList names => WriteNames(names, output),
            _ => 0,
        };
    }

    public int DocumentProperties(nint window, nint printerHandle, string deviceName, nint output, nint input, uint mode)
    {
        Calls.Add(nameof(DocumentProperties));

        if (Fails(nameof(DocumentProperties)))
        {
            return -1;
        }

        // The driver-private tail a real driver appends. Anything the driver does with the
        // size must survive it being larger than DEVMODEW.
        var size = Marshal.SizeOf<WindowsSpoolerInterop.DevMode>() + 64;

        if (mode == 0)
        {
            return size;
        }

        // DM_IN_BUFFER: the driver gave a filled device mode to reconcile. Reading it back
        // is how a test sees what the job asked the spooler for.
        if ((mode & 8) != 0 && input != 0)
        {
            AcceptedDeviceMode = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(input);
        }

        if ((mode & 2) != 0 && output != 0)
        {
            var reported = DeviceMode;
            if (ShortDeviceModeSize is ushort shortSize)
            {
                reported.Size = shortSize;
            }

            Marshal.StructureToPtr(reported, output, false);
        }

        return 1;
    }

    private bool Fails(string call)
    {
        if (!String.Equals(FailingCall, call, StringComparison.Ordinal))
        {
            return false;
        }

        LastError = FailureError;
        return true;
    }

    private static int WriteWords(short[] words, nint output)
    {
        if (output != 0)
        {
            Marshal.Copy(words, 0, output, words.Length);
        }

        return words.Length;
    }

    private static int WriteNames(NameList names, nint output)
    {
        if (output != 0)
        {
            for (var i = 0; i < names.Names.Count; i++)
            {
                // Fixed-width blocks, blank padded and not necessarily terminated, which is
                // what DC_PAPERNAMES and DC_BINNAMES return.
                var block = names.Names[i].PadRight(names.BlockLength, '\0');
                var chars = block[..names.BlockLength].ToCharArray();
                Marshal.Copy(chars, 0, output + (i * names.BlockLength * 2), chars.Length);
            }
        }

        return names.Names.Count;
    }

    /// <summary>A DC_*NAMES answer: fixed-width character blocks, back to back.</summary>
    internal sealed record NameList(IReadOnlyList<string> Names, int BlockLength);
}
