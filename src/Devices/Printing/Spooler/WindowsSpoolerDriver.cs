using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdaptArch.Devices.Printing.Spooler;

// The spooler RPC runs on the caller thread and cannot be interrupted, so each method
// checks the cancellation token on entry only.
[SupportedOSPlatform("windows")]
internal sealed class WindowsSpoolerDriver : ISpoolerDriver
{
    // EnumJobs takes this as a plain limit, not an allocation size.
    private const int JobEnumerationLimit = Int32.MaxValue;

    // ERROR_INVALID_PARAMETER: SetJob reports this for a job that left the queue.
    private const int ErrorInvalidParameter = 87;

    // ERROR_INSUFFICIENT_BUFFER: the expected answer of a size query with an empty buffer.
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

    // winspool.h PRINTER_ATTRIBUTE_DEFAULT and PRINTER_ATTRIBUTE_SHARED.
    private const uint PrinterAttributeDefault = 0x00000004;
    private const uint PrinterAttributeShared = 0x00000008;

    // The port name is what links a queue to the device behind it, and it lives in
    // PRINTER_INFO_2. The enumeration deliberately stays at level 4: EnumPrinters at
    // level 2 opens every remote connection over RPC, so one dead print server would
    // stall the whole discovery until the call times out. Reading one queue at a time,
    // only when the caller asked for identities, keeps that cost where it belongs.
    public Task<PrinterIdentity?> GetIdentityAsync(string queueName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        cancellationToken.ThrowIfCancellationRequested();

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
            PrinterIdentity identity = new()
            {
                Name = info.Comment,
                Location = info.Location,
                DeviceUri = info.PortName,
                Aliases = SpoolerAliases.FromPortName(info.PortName),
                IsDefault = (info.Attributes & PrinterAttributeDefault) != 0,
                IsShared = (info.Attributes & PrinterAttributeShared) != 0,
            };
            return Task.FromResult<PrinterIdentity?>(identity.IsEmpty ? null : identity);
        }
        catch (InvalidOperationException)
        {
            // A queue that cannot be opened tells nothing about its device. That is not
            // a failure of the discovery that found it.
            return Task.FromResult<PrinterIdentity?>(null);
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

    private static DiscoveredPrinter MapDiscovered(string name)
    {
        var id = PrinterId.ForSpooler(name);
        return new DiscoveredPrinter(id, new SpoolerPrinterEndpoint(name), new PrinterInfo(id, name)) { Source = DiscoverySource.Spooler };
    }

    // Data type RAW: printer languages such as ZPL pass through unchanged. The mapped
    // options travel in a DEVMODE built by the driver; the rest go to DroppedOptions.
    // Copies are printed as one document each, because a RAW queue never reads dmCopies,
    // and the reported job is the first of them.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        var request = WindowsSpoolerDeviceModeMapper.Build(options, MediaFor(queueName, options), SourcesFor(queueName, options));
        var copies = options?.Copies ?? 1;
        var bytes = payload.Data.ToArray();

        var dataTypePtr = IntPtr.Zero;
        var jobNamePtr = IntPtr.Zero;
        var deviceMode = IntPtr.Zero;
        var printerHandle = IntPtr.Zero;
        try
        {
            dataTypePtr = Marshal.StringToHGlobalUni("RAW");
            jobNamePtr = Marshal.StringToHGlobalUni(options?.JobName ?? queueName);

            // The device mode must exist before the printing handle is opened, because
            // PRINTER_DEFAULTS is what carries it onto every job started on that handle.
            deviceMode = BuildDeviceMode(queueName, request);
            var defaults = new WindowsSpoolerInterop.PrinterDefaults
            {
                DataType = dataTypePtr,
                DevMode = deviceMode,
                DesiredAccess = WindowsSpoolerInterop.PrinterAccessUse,
            };

            if (!WindowsSpoolerInterop.OpenPrinter(queueName, out printerHandle, in defaults))
            {
                // OpenPrinter leaves the out handle undefined on failure.
                printerHandle = IntPtr.Zero;
                ThrowLastError(nameof(WindowsSpoolerInterop.OpenPrinter));
            }

            var documentInfo = new WindowsSpoolerInterop.DocInfo1
            {
                DocName = jobNamePtr,
                OutputFile = 0,
                DataType = dataTypePtr,
            };

            var firstJobId = 0;
            for (var copy = 0; copy < copies; copy++)
            {
                var jobId = SubmitDocument(printerHandle, documentInfo, bytes);
                if (copy == 0)
                {
                    firstJobId = jobId;
                }
            }

            return Task.FromResult(new PrintJobInfo(firstJobId.ToString(CultureInfo.InvariantCulture), PrinterId.ForSpooler(queueName), PrintJobState.Queued)
            {
                JobName = options?.JobName,
                Detail = copies == 1 ? null : $"Copy 1 of {copies}. Each copy is a separate spooler job.",
                DroppedOptions = request.Dropped,
            });
        }
        finally
        {
            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }

            FreeIfSet(deviceMode);
            FreeIfSet(jobNamePtr);
            FreeIfSet(dataTypePtr);
        }
    }

    // One document: start, write, end. A failure after StartDocPrinter deletes the job, so
    // no truncated document commits. A failure on a later copy leaves the copies before it
    // in the queue, which is the same as a paper jam after the first copy.
    private static int SubmitDocument(nint printerHandle, WindowsSpoolerInterop.DocInfo1 documentInfo, byte[] bytes)
    {
        var jobId = 0;
        var pageStarted = false;
        var written = false;
        try
        {
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
            WriteAll(printerHandle, bytes);
            written = true;
            return jobId;
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
        }
    }

    // The name-to-number lists cost two spooler calls each, so they are read only when the
    // job actually names a media size or a tray.
    private static IReadOnlyList<PrinterMedia> MediaFor(string queueName, PrintOptions? options) =>
        options?.MediaSize is null ? [] : ReadMedia(queueName);

    private static IReadOnlyList<PrinterMediaSource> SourcesFor(string queueName, PrintOptions? options) =>
        options?.MediaSource is null ? [] : ReadMediaSources(queueName);

    // A DEVMODE has a driver-private tail that only the driver can fill, so the queue
    // default is asked for first, the mapped fields are written over it, and a second call
    // lets the driver reconcile what it was given. The answer is unmanaged memory the
    // caller frees, or zero when the job asked for nothing a field can carry.
    private static nint BuildDeviceMode(string queueName, DeviceModeRequest request)
    {
        if (request.IsEmpty)
        {
            return IntPtr.Zero;
        }

        var printerHandle = IntPtr.Zero;
        var draft = IntPtr.Zero;
        var accepted = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            // The answer is sizeof(DEVMODEW) plus the driver-private tail, so anything
            // smaller than DEVMODEW is a refusal, not a short buffer.
            var wholeSize = Marshal.SizeOf<WindowsSpoolerInterop.DevMode>();
            var size = WindowsSpoolerInterop.DocumentProperties(0, printerHandle, queueName, 0, 0, 0);
            if (size < wholeSize)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.DocumentProperties));
            }

            draft = Marshal.AllocHGlobal(size);
            if (WindowsSpoolerInterop.DocumentProperties(0, printerHandle, queueName, draft, 0, WindowsSpoolerInterop.DmOutBuffer) < 0)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.DocumentProperties));
            }

            var deviceMode = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(draft);
            if (deviceMode.Size < wholeSize)
            {
                // Writing the fields back would overwrite the tail that sits behind a
                // device mode this short. No driver in use reports one.
                throw new InvalidOperationException(
                    $"The driver of '{queueName}' reported a {deviceMode.Size} byte device mode, and {wholeSize} bytes are needed.");
            }

            WriteRequest(ref deviceMode, request);
            Marshal.StructureToPtr(deviceMode, draft, false);

            // The driver reconciles the draft into a second buffer. One buffer for both
            // directions would ask it to read and write the same bytes.
            accepted = Marshal.AllocHGlobal(size);
            if (WindowsSpoolerInterop.DocumentProperties(
                    0, printerHandle, queueName, accepted, draft, WindowsSpoolerInterop.DmInBuffer | WindowsSpoolerInterop.DmOutBuffer) < 0)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.DocumentProperties));
            }

            var result = accepted;
            accepted = IntPtr.Zero;
            return result;
        }
        finally
        {
            FreeIfSet(accepted);
            FreeIfSet(draft);
            if (printerHandle != IntPtr.Zero)
            {
                _ = WindowsSpoolerInterop.ClosePrinter(printerHandle);
            }
        }
    }

    // dmFields says which fields the driver must read, so the bits and the values are set
    // together. A field the job did not ask for keeps the queue default.
    private static void WriteRequest(ref WindowsSpoolerInterop.DevMode deviceMode, DeviceModeRequest request)
    {
        deviceMode.Fields |= request.Fields;
        deviceMode.Orientation = request.Orientation ?? deviceMode.Orientation;
        deviceMode.Scale = request.Scale ?? deviceMode.Scale;
        deviceMode.PaperSize = request.PaperSize ?? deviceMode.PaperSize;
        deviceMode.DefaultSource = request.DefaultSource ?? deviceMode.DefaultSource;
        deviceMode.PrintQuality = request.PrintQuality ?? deviceMode.PrintQuality;
        deviceMode.YResolution = request.YResolution ?? deviceMode.YResolution;
        deviceMode.Color = request.Color ?? deviceMode.Color;
        deviceMode.Duplex = request.Duplex ?? deviceMode.Duplex;
    }

    // WritePrinter can write fewer bytes than asked. The pin needs no unsafe code.
    private static void WriteAll(nint printerHandle, byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            var remaining = bytes.Length;
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
        finally
        {
            handle.Free();
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
            return Task.FromResult(new PrinterStatus(PrinterId.ForSpooler(queueName), state)
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

    // A buffer-returning capability is queried twice: once for the count, once for the data.
    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var media = ReadMedia(queueName);
        var mediaSources = ReadMediaSources(queueName);
        var defaults = ReadDeviceModeDefaults(queueName, media, mediaSources);

        List<string> mediaSizes = new(media.Count);
        foreach (var entry in media)
        {
            mediaSizes.Add(entry.Name);
        }

        var configuration = new PrinterConfiguration(PrinterId.ForSpooler(queueName))
        {
            SupportsDuplex = QueryCapability(queueName, WindowsSpoolerInterop.DcDuplex, 0) == 1,
            SupportsColor = QueryCapability(queueName, WindowsSpoolerInterop.DcColorDevice, 0) == 1,
            SupportedResolutionsDpi = ReadResolutions(queueName),
            MediaSizes = mediaSizes,
            Media = media,
            MediaSources = mediaSources,
            DefaultMediaSize = defaults.MediaSize,
            DefaultMediaSource = defaults.MediaSource,
            DefaultOrientation = defaults.Orientation,
            DefaultResolutionDpi = defaults.ResolutionDpi,
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

            // The second call can report more entries than the first; clamp to the allocation.
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

    private static IReadOnlyList<PrinterMedia> ReadMedia(string queueName)
    {
        var names = ReadNames(queueName, WindowsSpoolerInterop.DcPaperNames, WindowsSpoolerCapabilityParser.PaperNameBlockLength);
        return WindowsSpoolerCapabilityParser.PairMedia(names, ReadWords(queueName, WindowsSpoolerInterop.DcPapers));
    }

    private static IReadOnlyList<PrinterMediaSource> ReadMediaSources(string queueName)
    {
        var names = ReadNames(queueName, WindowsSpoolerInterop.DcBinNames, WindowsSpoolerCapabilityParser.BinNameBlockLength);
        return WindowsSpoolerCapabilityParser.PairMediaSources(names, ReadWords(queueName, WindowsSpoolerInterop.DcBins));
    }

    private static IReadOnlyList<string> ReadNames(string queueName, ushort capability, int blockLength)
    {
        var count = QueryCapability(queueName, capability, 0);
        if (count == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * blockLength * sizeof(char));
        try
        {
            var written = QueryCapability(queueName, capability, buffer);
            if (written == 0)
            {
                return [];
            }

            // Clamp for the same reason as ReadResolutions above.
            written = Math.Min(written, count);
            var chars = new char[written * blockLength];
            Marshal.Copy(buffer, chars, 0, chars.Length);
            return WindowsSpoolerCapabilityParser.ParseNames(chars, written, blockLength);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // DC_PAPERS and DC_BINS answer with WORD values, which Marshal.Copy reads as short.
    private static IReadOnlyList<int> ReadWords(string queueName, ushort capability)
    {
        var count = QueryCapability(queueName, capability, 0);
        if (count == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(count * sizeof(short));
        try
        {
            var written = QueryCapability(queueName, capability, buffer);
            if (written == 0)
            {
                return [];
            }

            written = Math.Min(written, count);
            var words = new short[written];
            Marshal.Copy(buffer, words, 0, words.Length);
            return WindowsSpoolerCapabilityParser.ParseWords(words);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // The device mode holds what the queue does when a job asks for nothing. Its pointer
    // belongs to the GetPrinter buffer, so it is read before that buffer is freed.
    private static DeviceModeDefaults ReadDeviceModeDefaults(
        string queueName,
        IReadOnlyList<PrinterMedia> media,
        IReadOnlyList<PrinterMediaSource> sources)
    {
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
            if (info.DevMode == IntPtr.Zero)
            {
                // A queue can report no device mode at all, which denies no default.
                return new DeviceModeDefaults(null, null, null, null);
            }

            var deviceMode = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(info.DevMode);
            return WindowsSpoolerCapabilityParser.ReadDefaults(
                deviceMode.Fields,
                deviceMode.Orientation,
                deviceMode.PaperSize,
                deviceMode.DefaultSource,
                deviceMode.PrintQuality,
                deviceMode.YResolution,
                media,
                sources);
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
            var printerId = PrinterId.ForSpooler(queueName);
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

    // EnumJobs addresses jobs by queue position, so there is no single-job lookup.
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
            // OpenPrinter leaves the out handle undefined on failure.
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
