using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// Reaches the Windows print spooler through winspool.drv. Windows has no local IPP
// server the way CUPS does, so this is the one driver that needs native interop; see
// WindowsSpoolerInterop for the P/Invoke declarations and struct layouts, and
// WindowsSpoolerStatusMapper / WindowsSpoolerCapabilityParser for the pure, testable
// logic this driver calls into once a native buffer has been read.
//
// Every public method starts with an OperatingSystem.IsWindows() check, both so the
// platform compatibility analyzer accepts the calls into WindowsSpoolerInterop below
// it, and so a caller on Linux or macOS gets a clear PlatformNotSupportedException
// instead of a native load failure.
//
// The spooler RPC runs on the caller thread and cannot be interrupted, so each method
// checks the cancellation token on entry only.
[SupportedOSPlatform("windows")]
internal sealed class WindowsSpoolerDriver : ISpoolerDriver
{
    // A large, but finite, upper bound on how many jobs EnumJobs can enumerate in one
    // call. The API takes this as a plain limit, not an allocation size.
    private const int JobEnumerationLimit = Int32.MaxValue;

    // ERROR_INVALID_PARAMETER: what SetJob reports for a job identifier that is no
    // longer in the queue.
    private const int ErrorInvalidParameter = 87;

    // ERROR_INSUFFICIENT_BUFFER: the expected answer of a size query made with an empty
    // buffer. Any other failure of that query is a real error.
    private const int ErrorInsufficientBuffer = 122;

    private const string WindowsOnlyMessage = "The Windows spooler driver needs Windows.";

    public Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var buffer = IntPtr.Zero;
        try
        {
            if (!WindowsSpoolerInterop.EnumPrinters(WindowsSpoolerInterop.PrinterEnumLocalAndConnections, 0, 4, 0, 0, out var needed, out _)
                && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumPrinters));
            }

            if (needed == 0)
            {
                return Task.FromResult<IReadOnlyList<DiscoveredPrinter>>([]);
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (!WindowsSpoolerInterop.EnumPrinters(WindowsSpoolerInterop.PrinterEnumLocalAndConnections, 0, 4, buffer, needed, out _, out var returned))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumPrinters));
            }

            var itemSize = Marshal.SizeOf<WindowsSpoolerInterop.PrinterInfo4>();
            List<DiscoveredPrinter> printers = new(returned);
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<WindowsSpoolerInterop.PrinterInfo4>(buffer + (i * itemSize));
                if (!String.IsNullOrWhiteSpace(info.PrinterName))
                {
                    printers.Add(MapDiscovered(info.PrinterName));
                }
            }

            return Task.FromResult<IReadOnlyList<DiscoveredPrinter>>(printers);
        }
        finally
        {
            FreeIfSet(buffer);
        }
    }

    private static DiscoveredPrinter MapDiscovered(string name)
    {
        var id = PrinterId.FromSpooler(name);
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(name), new PrinterInfo(id, name)) { Source = DiscoverySource.Spooler };
    }

    // Submission uses data type RAW: this library sends printer languages such as ZPL
    // and ESC/POS straight through, unmodified, in the order OpenPrinter,
    // StartDocPrinter, StartPagePrinter, WritePrinter, EndPagePrinter, EndDocPrinter,
    // ClosePrinter. The returned job identifier is whatever StartDocPrinter hands back.
    //
    // PrintOptions beyond JobName (Copies, Duplex, ColorMode, Orientation, MediaSource,
    // MediaSize, ResolutionDpi) are NOT mapped into a DEVMODE by this driver. Copies,
    // Duplex, ColorMode and Orientation live at fixed, stable offsets in DEVMODE, but
    // MediaSource and MediaSize are driver-specific numeric indices that would need a
    // second round trip (matching DC_BINNAMES / DC_PAPERNAMES) to resolve correctly,
    // and none of it can be exercised on this platform. This is a documented gap, not
    // a silent one: every set option except JobName is reported in
    // PrintJobInfo.DroppedOptions, and even a fully mapped DEVMODE only ever changes
    // the result when the queue's driver chooses to read it, since a RAW job reaches
    // the device unchanged either way.
    //
    // A failure after StartDocPrinter deletes the job before EndDocPrinter, so the
    // spooler never commits a truncated document.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        var dataTypePtr = IntPtr.Zero;
        var jobNamePtr = IntPtr.Zero;
        var printerHandle = IntPtr.Zero;
        var jobId = 0;
        var pageStarted = false;
        var written = false;
        try
        {
            dataTypePtr = Marshal.StringToHGlobalUni("RAW");
            jobNamePtr = Marshal.StringToHGlobalUni(options?.JobName ?? queueName);
            var defaults = new WindowsSpoolerInterop.PrinterDefaults
            {
                DataType = dataTypePtr,
                DevMode = 0,
                DesiredAccess = WindowsSpoolerInterop.PrinterAccessUse,
            };

            if (!WindowsSpoolerInterop.OpenPrinter(queueName, out printerHandle, in defaults))
            {
                // OpenPrinter leaves the out handle undefined on failure, not
                // guaranteed zero, so the finally block below must not trust it.
                printerHandle = IntPtr.Zero;
                ThrowLastError(nameof(WindowsSpoolerInterop.OpenPrinter));
            }

            var documentInfo = new WindowsSpoolerInterop.DocInfo1
            {
                DocName = jobNamePtr,
                OutputFile = 0,
                DataType = dataTypePtr,
            };

            jobId = WindowsSpoolerInterop.StartDocPrinter(printerHandle, 1, in documentInfo);
            if (jobId <= 0)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.StartDocPrinter));
            }

            if (!WindowsSpoolerInterop.StartPagePrinter(printerHandle))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.StartPagePrinter));
            }

            pageStarted = true;
            WriteAll(printerHandle, payload.Data);
            written = true;

            return Task.FromResult(new PrintJobInfo(jobId.ToString(CultureInfo.InvariantCulture), PrinterId.FromSpooler(queueName), PrintJobState.Queued)
            {
                JobName = options?.JobName,
                DroppedOptions = UnappliedOptions(options),
            });
        }
        finally
        {
            if (pageStarted)
            {
                _ = WindowsSpoolerInterop.EndPagePrinter(printerHandle);
            }

            if (jobId > 0)
            {
                if (!written)
                {
                    _ = WindowsSpoolerInterop.SetJob(printerHandle, jobId, 0, 0, WindowsSpoolerInterop.JobControlDelete);
                }

                _ = WindowsSpoolerInterop.EndDocPrinter(printerHandle);
            }

            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }

            FreeIfSet(jobNamePtr);
            FreeIfSet(dataTypePtr);
        }
    }

    // WritePrinter can write fewer bytes than asked, so it is called until every byte
    // is out. The payload is pinned in place instead of copied into a native buffer.
    private static unsafe void WriteAll(nint printerHandle, ReadOnlyMemory<byte> data)
    {
        using var pin = data.Pin();
        var pointer = (nint)pin.Pointer;
        var remaining = data.Length;
        while (remaining > 0)
        {
            if (!WindowsSpoolerInterop.WritePrinter(printerHandle, pointer, remaining, out var written))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.WritePrinter));
            }

            if (written <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(WindowsSpoolerInterop.WritePrinter)} wrote 0 of {remaining} remaining bytes.");
            }

            pointer += written;
            remaining -= written;
        }
    }

    // Every option this driver does not map into a DEVMODE, named as PrintOptionValidator
    // names them, so PrintJobInfo.DroppedOptions tells the caller what did not apply.
    internal static IReadOnlyList<string> UnappliedOptions(PrintOptions? options)
    {
        if (options is null)
        {
            return [];
        }

        List<string> names = [];
        AddIfSet(names, options.Copies is not null, nameof(PrintOptions.Copies));
        AddIfSet(names, options.Duplex is not null, nameof(PrintOptions.Duplex));
        AddIfSet(names, options.ColorMode is not null, nameof(PrintOptions.ColorMode));
        AddIfSet(names, options.Orientation is not null, nameof(PrintOptions.Orientation));
        AddIfSet(names, options.MediaSource is not null, nameof(PrintOptions.MediaSource));
        AddIfSet(names, options.MediaSize is not null, nameof(PrintOptions.MediaSize));
        AddIfSet(names, options.ResolutionDpi is not null, nameof(PrintOptions.ResolutionDpi));
        return names;
    }

    private static void AddIfSet(List<string> names, bool isSet, string name)
    {
        if (isSet)
        {
            names.Add(name);
        }
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var printerHandle = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            _ = WindowsSpoolerInterop.GetPrinter(printerHandle, 2, 0, 0, out var needed);
            buffer = Marshal.AllocHGlobal(needed);
            if (!WindowsSpoolerInterop.GetPrinter(printerHandle, 2, buffer, needed, out _))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.GetPrinter));
            }

            var info = Marshal.PtrToStructure<WindowsSpoolerInterop.PrinterInfo2>(buffer);
            var state = WindowsSpoolerStatusMapper.MapPrinterStatus(info.Status, info.Attributes);
            return Task.FromResult(new PrinterStatus(PrinterId.FromSpooler(queueName), state)
            {
                IsAcceptingJobs = WindowsSpoolerStatusMapper.IsAcceptingJobs(info.Status, info.Attributes),
                Detail = WindowsSpoolerStatusMapper.DescribePrinterStatus(info.Status, info.Attributes),
            });
        }
        finally
        {
            FreeIfSet(buffer);
            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }
        }
    }

    // DeviceCapabilitiesW needs no printer handle: it is called with the queue name
    // directly. Each buffer-returning capability is called twice, once with a null
    // buffer to learn the count and once with a buffer of that size; DC_DUPLEX and
    // DC_COLORDEVICE return 1 or 0 directly and need no buffer at all. A negative
    // return is an error, for example a queue name that does not exist.
    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var configuration = new PrinterConfiguration(PrinterId.FromSpooler(queueName))
        {
            SupportsDuplex = QueryCapability(queueName, WindowsSpoolerInterop.DcDuplex, 0) == 1,
            SupportsColor = QueryCapability(queueName, WindowsSpoolerInterop.DcColorDevice, 0) == 1,
            SupportedResolutionsDpi = ReadResolutions(queueName),
            MediaSizes = ReadPaperNames(queueName),
        };

        return Task.FromResult(configuration);
    }

    private static int QueryCapability(string queueName, ushort capability, nint output)
    {
        var result = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, capability, output, 0);
        if (result < 0)
        {
            ThrowLastError(nameof(WindowsSpoolerInterop.DeviceCapabilities));
        }

        return result;
    }

    private static IReadOnlyList<int> ReadResolutions(string queueName)
    {
        var count = QueryCapability(queueName, WindowsSpoolerInterop.DcEnumResolutions, 0);
        if (count == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * 2 * sizeof(int));
        try
        {
            var written = QueryCapability(queueName, WindowsSpoolerInterop.DcEnumResolutions, buffer);
            if (written == 0)
            {
                return [];
            }

            // The queue can report more entries on the second call than the first, if
            // its driver changes state in between. Clamp to what was actually
            // allocated, or the copy below reads past the buffer.
            written = Math.Min(written, count);
            var pairs = new int[written * 2];
            Marshal.Copy(buffer, pairs, 0, pairs.Length);
            return WindowsSpoolerCapabilityParser.ParseResolutions(pairs);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> ReadPaperNames(string queueName)
    {
        const int BlockLength = 64;
        var count = QueryCapability(queueName, WindowsSpoolerInterop.DcPaperNames, 0);
        if (count == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * BlockLength * sizeof(char));
        try
        {
            var written = QueryCapability(queueName, WindowsSpoolerInterop.DcPaperNames, buffer);
            if (written == 0)
            {
                return [];
            }

            // Clamp for the same reason as ReadResolutions above.
            written = Math.Min(written, count);
            var chars = new char[written * BlockLength];
            Marshal.Copy(buffer, chars, 0, chars.Length);
            return WindowsSpoolerCapabilityParser.ParsePaperNames(chars, written);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var printerHandle = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            if (!WindowsSpoolerInterop.EnumJobs(printerHandle, 0, JobEnumerationLimit, 2, 0, 0, out var needed, out _)
                && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumJobs));
            }

            if (needed == 0)
            {
                return Task.FromResult<IReadOnlyList<PrintJobInfo>>([]);
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (!WindowsSpoolerInterop.EnumJobs(printerHandle, 0, JobEnumerationLimit, 2, buffer, needed, out _, out var returned))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumJobs));
            }

            var itemSize = Marshal.SizeOf<WindowsSpoolerInterop.JobInfo2>();
            var printerId = PrinterId.FromSpooler(queueName);
            List<PrintJobInfo> jobs = new(returned);
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<WindowsSpoolerInterop.JobInfo2>(buffer + (i * itemSize));
                jobs.Add(MapJob(printerId, info));
            }

            return Task.FromResult<IReadOnlyList<PrintJobInfo>>(jobs);
        }
        finally
        {
            FreeIfSet(buffer);
            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }
        }
    }

    private static PrintJobInfo MapJob(PrinterId printerId, WindowsSpoolerInterop.JobInfo2 info)
    {
        var state = WindowsSpoolerStatusMapper.MapJobStatus(info.Status);
        return new PrintJobInfo(info.JobId.ToString(CultureInfo.InvariantCulture), printerId, state)
        {
            JobName = info.Document,
            TotalImpressions = info.TotalPages == 0 ? null : (int)info.TotalPages,
            ImpressionsCompleted = (int)info.PagesPrinted,
            Detail = WindowsSpoolerStatusMapper.DescribeJobStatus(info.Status),
        };
    }

    // EnumJobs addresses jobs by their position in the queue, not by job identifier,
    // and there is no separate single-job lookup declared in WindowsSpoolerInterop, so
    // this reuses the same enumeration GetJobsAsync uses and filters by identifier.
    public async Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var jobs = await GetJobsAsync(queueName, cancellationToken).ConfigureAwait(false);
        return jobs.FirstOrDefault(job => String.Equals(job.JobId, jobId, StringComparison.Ordinal));
    }

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        if (!Int32.TryParse(jobId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return Task.FromResult(false);
        }

        var printerHandle = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            if (!WindowsSpoolerInterop.SetJob(printerHandle, id, 0, 0, WindowsSpoolerInterop.JobControlCancel))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorInvalidParameter)
                {
                    return Task.FromResult(false);
                }

                throw new InvalidOperationException(Describe(nameof(WindowsSpoolerInterop.SetJob), error));
            }

            return Task.FromResult(true);
        }
        finally
        {
            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }
        }
    }

    private static void OpenForUse(string queueName, out nint printerHandle)
    {
        var defaults = new WindowsSpoolerInterop.PrinterDefaults
        {
            DataType = 0,
            DevMode = 0,
            DesiredAccess = WindowsSpoolerInterop.PrinterAccessUse,
        };

        if (!WindowsSpoolerInterop.OpenPrinter(queueName, out printerHandle, in defaults))
        {
            // OpenPrinter leaves the out handle undefined on failure, not guaranteed
            // zero, so every finally block that guards cleanup on this handle must
            // not trust a garbage value here.
            printerHandle = IntPtr.Zero;
            ThrowLastError(nameof(WindowsSpoolerInterop.OpenPrinter));
        }
    }

    private static void FreeIfSet(nint buffer)
    {
        if (buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ThrowLastError(string operation) =>
        throw new InvalidOperationException(Describe(operation, Marshal.GetLastWin32Error()));

    // GetPInvokeErrorMessage needs no reflection, so it is safe under native AOT.
    private static string Describe(string operation, int error) =>
        $"{operation} failed with Win32 error {error}: {Marshal.GetPInvokeErrorMessage(error)}";
}
