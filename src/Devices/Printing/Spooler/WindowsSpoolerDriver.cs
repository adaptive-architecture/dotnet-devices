using System.Globalization;
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
[SupportedOSPlatform("windows")]
internal sealed class WindowsSpoolerDriver : ISpoolerDriver
{
    // A large, but finite, upper bound on how many jobs EnumJobs can enumerate in one
    // call. The API takes this as a plain limit, not an allocation size.
    private const int JobEnumerationLimit = Int32.MaxValue;

    // ERROR_INVALID_PARAMETER: what SetJob reports for a job identifier that is no
    // longer in the queue.
    private const int ErrorInvalidParameter = 87;

    public Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

        var buffer = IntPtr.Zero;
        try
        {
            _ = WindowsSpoolerInterop.EnumPrinters(WindowsSpoolerInterop.PrinterEnumLocalAndConnections, 0, 4, 0, 0, out var needed, out _);
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
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(name), new PrinterInfo(id, name));
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
    // a silent one: even a fully mapped DEVMODE only ever changes the result when the
    // queue's driver chooses to read it, since a RAW job reaches the device unchanged
    // either way. This matches UnsupportedOptionBehavior.Send: the request goes out
    // and the printer decides.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        var dataTypePtr = Marshal.StringToHGlobalUni("RAW");
        var jobNamePtr = Marshal.StringToHGlobalUni(options?.JobName ?? queueName);
        var dataPtr = IntPtr.Zero;
        var printerHandle = IntPtr.Zero;
        var docStarted = false;
        var pageStarted = false;
        try
        {
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

            var jobId = WindowsSpoolerInterop.StartDocPrinter(printerHandle, 1, in documentInfo);
            if (jobId <= 0)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.StartDocPrinter));
            }

            docStarted = true;

            if (!WindowsSpoolerInterop.StartPagePrinter(printerHandle))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.StartPagePrinter));
            }

            pageStarted = true;

            var data = payload.Data;
            dataPtr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data.ToArray(), 0, dataPtr, data.Length);

            if (!WindowsSpoolerInterop.WritePrinter(printerHandle, dataPtr, data.Length, out var written))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.WritePrinter));
            }

            if (written != data.Length)
            {
                throw new InvalidOperationException(
                    $"{nameof(WindowsSpoolerInterop.WritePrinter)} wrote {written} of {data.Length} bytes.");
            }

            return Task.FromResult(new PrintJobInfo(jobId.ToString(CultureInfo.InvariantCulture), PrinterId.FromSpooler(queueName), PrintJobState.Queued)
            {
                JobName = options?.JobName,
            });
        }
        finally
        {
            if (pageStarted)
            {
                _ = WindowsSpoolerInterop.EndPagePrinter(printerHandle);
            }

            if (docStarted)
            {
                _ = WindowsSpoolerInterop.EndDocPrinter(printerHandle);
            }

            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }

            FreeIfSet(dataPtr);
            Marshal.FreeHGlobal(jobNamePtr);
            Marshal.FreeHGlobal(dataTypePtr);
        }
    }

    public Task<PrinterStatus> GetStatusAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

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
            var state = WindowsSpoolerStatusMapper.MapPrinterStatus(info.Status);
            return Task.FromResult(new PrinterStatus(PrinterId.FromSpooler(queueName), state)
            {
                IsAcceptingJobs = WindowsSpoolerStatusMapper.IsAcceptingJobs(info.Status),
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
    // DC_COLORDEVICE return 1 or 0 directly and need no buffer at all.
    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var configuration = new PrinterConfiguration(PrinterId.FromSpooler(queueName))
        {
            SupportsDuplex = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcDuplex, 0, 0) == 1,
            SupportsColor = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcColorDevice, 0, 0) == 1,
            SupportedResolutionsDpi = ReadResolutions(queueName),
            MediaSizes = ReadPaperNames(queueName),
        };

        return Task.FromResult(configuration);
    }

    private static IReadOnlyList<int> ReadResolutions(string queueName)
    {
        var count = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcEnumResolutions, 0, 0);
        if (count <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * 2 * sizeof(int));
        try
        {
            var written = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcEnumResolutions, buffer, 0);
            if (written <= 0)
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
        var count = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcPaperNames, 0, 0);
        if (count <= 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * BlockLength * sizeof(char));
        try
        {
            var written = WindowsSpoolerInterop.DeviceCapabilities(queueName, null, WindowsSpoolerInterop.DcPaperNames, buffer, 0);
            if (written <= 0)
            {
                return [];
            }

            // The queue can report more entries on the second call than the first, if
            // its driver changes state in between. Clamp to what was actually
            // allocated, or the copy below reads past the buffer.
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
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var printerHandle = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            _ = WindowsSpoolerInterop.EnumJobs(printerHandle, 0, JobEnumerationLimit, 2, 0, 0, out var needed, out _);
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
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var jobs = await GetJobsAsync(queueName, cancellationToken).ConfigureAwait(false);
        foreach (var job in jobs)
        {
            if (String.Equals(job.JobId, jobId, StringComparison.Ordinal))
            {
                return job;
            }
        }

        return null;
    }

    public Task<bool> CancelJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows spooler driver needs Windows.");
        }

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

                throw new InvalidOperationException($"{nameof(WindowsSpoolerInterop.SetJob)} failed with Win32 error {error}.");
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
        throw new InvalidOperationException($"{operation} failed with Win32 error {Marshal.GetLastWin32Error()}.");
}
