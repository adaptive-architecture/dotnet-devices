using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

// The spooler RPC runs on the caller thread and cannot be interrupted, so each method
// checks the cancellation token on entry only.
//
// No platform attribute: every native call goes through IWindowsSpoolerInterop, and the
// buffer protocol, the structure layouts and the error handling around it are managed code
// that runs anywhere. The platform is a constructor argument, and the public constructor
// answers it with OperatingSystem.IsWindows().
internal sealed class WindowsSpoolerDriver : ISpoolerDriver
{
    // EnumJobs takes this as a plain limit, not an allocation size.
    private const int JobEnumerationLimit = Int32.MaxValue;

    // ERROR_INVALID_PARAMETER: SetJob reports this for a job that left the queue.
    private const int ErrorInvalidParameter = 87;

    // ERROR_INSUFFICIENT_BUFFER: the expected answer of a size query with an empty buffer.
    private const int ErrorInsufficientBuffer = 122;

    private const string WindowsOnlyMessage = "The Windows spooler driver needs Windows.";

    private readonly PrintFormatPolicy _formats;
    private readonly ILogger _logger;
    private readonly IWindowsSpoolerInterop _interop;
    private readonly IWindowsGdiImagePrinter _images;
    private readonly bool _isWindows;

    public WindowsSpoolerDriver(PrintFormatPolicy? formats = null, ILoggerFactory? loggerFactory = null)
        : this(WindowsSpoolerInteropAdapter.Instance, new WindowsGdiImagePrinter(), OperatingSystem.IsWindows(), formats, loggerFactory)
    {
    }

    // The seam. The platform answer is given rather than asked for, so a test on Linux can
    // run everything the guard protects; see IWindowsSpoolerInterop for why that is safe.
    internal WindowsSpoolerDriver(
        IWindowsSpoolerInterop interop,
        IWindowsGdiImagePrinter images,
        bool isWindows,
        PrintFormatPolicy? formats = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(interop);
        ArgumentNullException.ThrowIfNull(images);
        _interop = interop;
        _images = images;
        _isWindows = isWindows;
        _formats = formats ?? PrintFormatPolicy.Default;
        _logger = SpoolerLog.Create(loggerFactory);
    }

    public Task<IReadOnlyList<DiscoveredPrinter>> EnumeratePrintersAsync(CancellationToken cancellationToken)
    {
        if (!_isWindows)
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var buffer = IntPtr.Zero;
        try
        {
            if (!_interop.EnumPrinters(WindowsSpoolerInterop.PrinterEnumLocalAndConnections, 0, 4, 0, 0, out var needed, out _)
                && _interop.GetLastError() != ErrorInsufficientBuffer)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumPrinters));
            }

            if (needed == 0)
            {
                return Task.FromResult<IReadOnlyList<DiscoveredPrinter>>([]);
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (!_interop.EnumPrinters(WindowsSpoolerInterop.PrinterEnumLocalAndConnections, 0, 4, buffer, needed, out _, out var returned))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumPrinters));
            }

            var itemSize = Marshal.SizeOf<WindowsSpoolerInterop.PrinterInfo4>();
            List<DiscoveredPrinter> printers = new(returned);
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<WindowsSpoolerInterop.PrinterInfo4>(buffer + (i * itemSize));
                if (String.IsNullOrWhiteSpace(info.PrinterName))
                {
                    continue;
                }

                DiscoveredPrinter printer;
                try
                {
                    printer = MapDiscovered(info.PrinterName);
                }
                catch (ArgumentException exception)
                {
                    // Windows accepts names no identifier can carry, such as the
                    // in-box "Generic / Text Only". One of them must not hide the
                    // queues beside it, so it is skipped and reported instead.
                    SpoolerLog.QueueNotListed(_logger, info.PrinterName, exception);
                    continue;
                }

                printers.Add(printer);
            }

            SpoolerLog.QueuesEnumerated(_logger, printers.Count);
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

            _ = _interop.GetPrinter(printerHandle, 2, 0, 0, out var needed);
            buffer = Marshal.AllocHGlobal(needed);
            if (!_interop.GetPrinter(printerHandle, 2, buffer, needed, out _))
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
        catch (InvalidOperationException exception)
        {
            // A queue that cannot be opened tells nothing about its device. That is not
            // a failure of the discovery that found it. Error even so: the aliases this
            // read would have given are what group the queue with its printer, so a
            // caller silently sees one printer as two.
            SpoolerLog.QueueIdentityNotRead(_logger, queueName, exception);
            return Task.FromResult<PrinterIdentity?>(null);
        }
        finally
        {
            FreeIfSet(buffer);
            if (printerHandle != IntPtr.Zero)
            {
                _ = _interop.ClosePrinter(printerHandle);
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
    //
    // PNG and JPEG take a second path below: they are drawn onto a GDI printer device
    // context so the driver rasterises the page. Sent as RAW, they would reach a
    // firmware that reads only its own page language and print nothing, while the
    // spooler still reports success. PDF takes a third path: each page is rendered
    // to PNG with the in-box Windows engine first, then printed as one GDI document.
    public Task<PrintJobInfo> SubmitAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        if (!_isWindows)
        {
            throw new PlatformNotSupportedException(WindowsOnlyMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(payload);

        if (WindowsSpoolerContent.Classify(payload.ContentType, _formats) == SpoolerContentKind.Document)
        {
            return SubmitDocumentAsync(queueName, payload, options, cancellationToken);
        }

        if (WindowsSpoolerContent.Classify(payload.ContentType, _formats) == SpoolerContentKind.Image)
        {
            return SubmitImageAsync(queueName, payload, options, cancellationToken);
        }

        return SubmitRawAsync(queueName, payload, options, cancellationToken);
    }

    // One GDI document for the whole file: every selected page is converted to an image
    // first, so a corrupt file fails before any job exists. PageRanges is honoured here,
    // unlike on every other Windows path, because a page range names converted pages
    // rather than a device mode field.
    private async Task<PrintJobInfo> SubmitDocumentAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = WindowsSpoolerDeviceModeMapper.Build(options, MediaFor(queueName, options), SourcesFor(queueName, options));
        var imageRequest = WithoutImageLayout(request);
        var dropped = WithoutGdiDropped(request.Dropped, true);
        ReportDropped(queueName, dropped);
        var copies = options?.Copies ?? 1;
        var bytes = payload.Data.ToArray();
        var jobName = options?.JobName ?? queueName;

        var renderDpi = WindowsSpoolerContent.RenderDpi(options?.ResolutionDpi);
        var rendered = await ConvertAsync(
            queueName,
            payload.ContentType,
            bytes,
            renderDpi,
            options,
            cancellationToken).ConfigureAwait(false);

        var deviceMode = IntPtr.Zero;
        try
        {
            deviceMode = BuildDeviceMode(queueName, imageRequest);
            var jobId = _images.PrintPages(
                new WindowsGdiJob(
                    queueName,
                    WindowsSpoolerContent.FileExtension(PrinterContentTypes.Png),
                    jobName,
                    deviceMode,
                    copies,
                    options?.Orientation,
                    options?.Scaling,
                    renderDpi,
                    options?.Placement,
                    options?.Smoothing),
                rendered);

            SpoolerLog.JobSpooled(_logger, queueName, jobId, bytes.Length, payload.ContentType);
            return new PrintJobInfo(jobId.ToString(CultureInfo.InvariantCulture), PrinterId.ForSpooler(queueName), PrintJobState.Queued)
            {
                JobName = options?.JobName,
                DroppedOptions = dropped,
            };
        }
        finally
        {
            FreeIfSet(deviceMode);
        }
    }

    // One GDI job: the image is drawn onto a printer device context and the driver
    // rasterises it. Orientation and scaling are applied by the layout math, not by
    // the device mode, so both are kept out of DroppedOptions and out of the mode.
    // Copies travel as dmCopies, which the GDI path honours, so one job prints all.
    private Task<PrintJobInfo> SubmitImageAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var request = WindowsSpoolerDeviceModeMapper.Build(options, MediaFor(queueName, options), SourcesFor(queueName, options));
        var imageRequest = WithoutImageLayout(request);
        var dropped = WithoutGdiDropped(request.Dropped, false);
        ReportDropped(queueName, dropped);
        var copies = options?.Copies ?? 1;
        var bytes = payload.Data.ToArray();
        var jobName = options?.JobName ?? queueName;

        var deviceMode = IntPtr.Zero;
        try
        {
            deviceMode = BuildDeviceMode(queueName, imageRequest);
            var jobId = _images.Print(
                new WindowsGdiJob(
                    queueName,
                    WindowsSpoolerContent.FileExtension(payload.ContentType),
                    jobName,
                    deviceMode,
                    copies,
                    options?.Orientation,
                    options?.Scaling,
                    null,
                    options?.Placement,
                    options?.Smoothing),
                bytes);

            SpoolerLog.JobSpooled(_logger, queueName, jobId, bytes.Length, payload.ContentType);
            return Task.FromResult(new PrintJobInfo(jobId.ToString(CultureInfo.InvariantCulture), PrinterId.ForSpooler(queueName), PrintJobState.Queued)
            {
                JobName = options?.JobName,
                DroppedOptions = dropped,
            });
        }
        finally
        {
            FreeIfSet(deviceMode);
        }
    }

    // The device mode never carries the two options the image layout math owns.
    // Leaving them in would rotate and scale twice: once in the mode, once on
    // the page.
    private static DeviceModeRequest WithoutImageLayout(DeviceModeRequest request) =>
        new(
            request.Fields & ~(WindowsSpoolerCapabilityParser.DmOrientation | WindowsSpoolerCapabilityParser.DmScale),
            null,
            null,
            request.PaperSize,
            request.DefaultSource,
            request.PrintQuality,
            request.YResolution,
            request.Color,
            request.Duplex,
            WithoutGdiDropped(request.Dropped, false),
            request.PaperWidth,
            request.PaperLength);

    // Orientation and scaling are laid out on the GDI page, never in the device
    // mode. PageRanges is additionally honoured by the PDF render, which selects
    // pages rather than naming a mode field.
    // The join costs an allocation for each job, so it runs only when a reader wants it.
    private void ReportDropped(string queueName, List<string> dropped)
    {
        if (dropped.Count > 0 && _logger.IsEnabled(LogLevel.Warning))
        {
            SpoolerLog.DeviceModeOptionsDropped(_logger, queueName, String.Join(", ", dropped));
        }
    }

    private static List<string> WithoutGdiDropped(IReadOnlyList<string> dropped, bool honorPageRanges)
    {
        List<string> kept = new(dropped.Count);
        foreach (var name in dropped)
        {
            if (String.Equals(name, nameof(PrintOptions.Orientation), StringComparison.Ordinal)
                || String.Equals(name, nameof(PrintOptions.Scaling), StringComparison.Ordinal))
            {
                continue;
            }

            if (honorPageRanges && String.Equals(name, nameof(PrintOptions.PageRanges), StringComparison.Ordinal))
            {
                continue;
            }

            kept.Add(name);
        }

        return kept;
    }

    // A document format prints as images, so it needs a converter. Without one the job
    // fails here, before it exists, instead of spooling silence. PDF names the package
    // that carries the built-in converter, because that is the common case.
    private async Task<IReadOnlyList<byte[]>> ConvertAsync(
        string queueName,
        string contentType,
        byte[] data,
        int dpi,
        PrintOptions? options,
        CancellationToken cancellationToken)
    {
        var converterName = options?.ConverterName;
        var converter = _formats.ConverterFor(contentType, converterName);
        if (converter is null)
        {
            // A job that named a converter gets told which names exist; one that named none
            // gets told how to register the first.
            PrintConverters.ThrowIfNamed(_formats, contentType, converterName);

            throw new NotSupportedException(
                $"The Windows spooler cannot print '{contentType}' without a converter for it: add one to PrinterManagerOptions.Converters. " +
                $"For PDF, reference AdaptArch.Devices.Windows or AdaptArch.Devices.Pdfium and call EnablePdfPrinting(). Queue '{queueName}' spooled nothing.");
        }

        // A converter is chosen by what it reads, and GDI draws only PNG. One that reads this
        // format but writes something else -- a PWG Raster stream, say, for an IPP printer --
        // would otherwise be handed to GDI, which reads the first octets and draws nothing.
        if (!converter.CanEmit(PrinterContentTypes.Png))
        {
            throw new NotSupportedException(
                $"The converter of '{contentType}' does not write '{PrinterContentTypes.Png}', which is the only format the Windows spooler draws: " +
                $"register one that does. Queue '{queueName}' spooled nothing.");
        }

        // The media is deliberately absent: this path builds the device mode after it
        // converts, so it does not yet know the sheet, and it places the page itself at the
        // draw step. What the converter can still act on is how sharply it renders.
        PrintConversionContext context = new(contentType, PrinterContentTypes.Png, dpi, options?.PageRanges, queueName)
        {
            Scaling = options?.Scaling,
            Orientation = options?.Orientation,
            Smoothing = options?.Smoothing,
            MediaSizeSource = options?.MediaSizeSource ?? MediaSizeSource.Printer,
        };
        var pages = await converter.ConvertAsync(data, context, cancellationToken).ConfigureAwait(false);
        if (pages.Count == 0)
        {
            throw new InvalidOperationException(
                $"The converter of '{contentType}' returned no page, so queue '{queueName}' spooled nothing.");
        }

        return pages;
    }

    private Task<PrintJobInfo> SubmitRawAsync(string queueName, PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

            if (!_interop.OpenPrinter(queueName, out printerHandle, defaults))
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

            SpoolerLog.JobSpooled(_logger, queueName, firstJobId, bytes.Length, payload.ContentType);
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
                _ = _interop.ClosePrinter(printerHandle);
            }

            FreeIfSet(deviceMode);
            FreeIfSet(jobNamePtr);
            FreeIfSet(dataTypePtr);
        }
    }

    // One document: start, write, end. A failure after StartDocPrinter deletes the job, so
    // no truncated document commits. A failure on a later copy leaves the copies before it
    // in the queue, which is the same as a paper jam after the first copy.
    private int SubmitDocument(nint printerHandle, WindowsSpoolerInterop.DocInfo1 documentInfo, byte[] bytes)
    {
        var jobId = 0;
        var pageStarted = false;
        var written = false;
        try
        {
            jobId = _interop.StartDocPrinter(printerHandle, 1, documentInfo);
            if (jobId <= 0)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.StartDocPrinter));
            }

            if (!_interop.StartPagePrinter(printerHandle))
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
                _ = _interop.EndPagePrinter(printerHandle);
            }

            if (jobId > 0)
            {
                if (!written)
                {
                    _ = _interop.SetJob(printerHandle, jobId, 0, 0, WindowsSpoolerInterop.JobControlDelete);
                }

                _ = _interop.EndDocPrinter(printerHandle);
            }
        }
    }

    // The name-to-number lists cost two spooler calls each, so they are read only when the
    // job actually names a media size or a tray.
    private IReadOnlyList<PrinterMedia> MediaFor(string queueName, PrintOptions? options) =>
        options?.MediaSize is null ? [] : ReadMedia(queueName);

    private IReadOnlyList<PrinterMediaSource> SourcesFor(string queueName, PrintOptions? options) =>
        options?.MediaSource is null ? [] : ReadMediaSources(queueName);

    // A DEVMODE has a driver-private tail that only the driver can fill, so the queue
    // default is asked for first, the mapped fields are written over it, and a second call
    // lets the driver reconcile what it was given. The answer is unmanaged memory the
    // caller frees, or zero when the job asked for nothing a field can carry.
    private nint BuildDeviceMode(string queueName, DeviceModeRequest request)
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
            var size = _interop.DocumentProperties(0, printerHandle, queueName, 0, 0, 0);
            if (size < wholeSize)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.DocumentProperties));
            }

            draft = Marshal.AllocHGlobal(size);
            if (_interop.DocumentProperties(0, printerHandle, queueName, draft, 0, WindowsSpoolerInterop.DmOutBuffer) < 0)
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
            if (_interop.DocumentProperties(
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
                _ = _interop.ClosePrinter(printerHandle);
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
        deviceMode.PaperWidth = request.PaperWidth ?? deviceMode.PaperWidth;
        deviceMode.PaperLength = request.PaperLength ?? deviceMode.PaperLength;
        deviceMode.DefaultSource = request.DefaultSource ?? deviceMode.DefaultSource;
        deviceMode.PrintQuality = request.PrintQuality ?? deviceMode.PrintQuality;
        deviceMode.YResolution = request.YResolution ?? deviceMode.YResolution;
        deviceMode.Color = request.Color ?? deviceMode.Color;
        deviceMode.Duplex = request.Duplex ?? deviceMode.Duplex;
    }

    // WritePrinter can write fewer bytes than asked. The pin needs no unsafe code.
    private void WriteAll(nint printerHandle, byte[] bytes)
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var pointer = handle.AddrOfPinnedObject();
            var remaining = bytes.Length;
            while (remaining > 0)
            {
                if (!_interop.WritePrinter(printerHandle, pointer, remaining, out var written))
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
        if (!_isWindows)
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

            _ = _interop.GetPrinter(printerHandle, 2, 0, 0, out var needed);
            buffer = Marshal.AllocHGlobal(needed);
            if (!_interop.GetPrinter(printerHandle, 2, buffer, needed, out _))
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.GetPrinter));
            }

            var info = Marshal.PtrToStructure<WindowsSpoolerInterop.PrinterInfo2>(buffer);
            var state = WindowsSpoolerStatusMapper.MapPrinterStatus(info.Status, info.Attributes);
            return Task.FromResult(new PrinterStatus(PrinterId.ForSpooler(queueName), state)
            {
                IsAcceptingJobs = WindowsSpoolerStatusMapper.IsAcceptingJobs(info.Status, info.Attributes),
                Detail = WindowsSpoolerStatusMapper.DescribePrinterStatus(info.Status, info.Attributes),
                StateReasons = WindowsSpoolerStatusMapper.PrinterStateReasons(info.Status, info.Attributes),
            });
        }
        finally
        {
            FreeIfSet(buffer);
            if (printerHandle != IntPtr.Zero)
            {
                _ = _interop.ClosePrinter(printerHandle);
            }
        }
    }

    // A buffer-returning capability is queried twice: once for the count, once for the data.
    public Task<PrinterConfiguration> GetConfigurationAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!_isWindows)
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

    private int QueryCapability(string queueName, ushort capability, nint output)
    {
        var result = _interop.DeviceCapabilities(queueName, null, capability, output, 0);
        if (result < 0)
        {
            ThrowLastError(nameof(WindowsSpoolerInterop.DeviceCapabilities));
        }

        return result;
    }

    private IReadOnlyList<int> ReadResolutions(string queueName)
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

    private IReadOnlyList<PrinterMedia> ReadMedia(string queueName)
    {
        var names = ReadNames(queueName, WindowsSpoolerInterop.DcPaperNames, WindowsSpoolerCapabilityParser.PaperNameBlockLength);
        return WindowsSpoolerCapabilityParser.PairMedia(names, ReadWords(queueName, WindowsSpoolerInterop.DcPapers));
    }

    private IReadOnlyList<PrinterMediaSource> ReadMediaSources(string queueName)
    {
        var names = ReadNames(queueName, WindowsSpoolerInterop.DcBinNames, WindowsSpoolerCapabilityParser.BinNameBlockLength);
        return WindowsSpoolerCapabilityParser.PairMediaSources(names, ReadWords(queueName, WindowsSpoolerInterop.DcBins));
    }

    private IReadOnlyList<string> ReadNames(string queueName, ushort capability, int blockLength)
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
    private IReadOnlyList<int> ReadWords(string queueName, ushort capability)
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
    private DeviceModeDefaults ReadDeviceModeDefaults(
        string queueName,
        IReadOnlyList<PrinterMedia> media,
        IReadOnlyList<PrinterMediaSource> sources)
    {
        var printerHandle = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            OpenForUse(queueName, out printerHandle);

            _ = _interop.GetPrinter(printerHandle, 2, 0, 0, out var needed);
            buffer = Marshal.AllocHGlobal(needed);
            if (!_interop.GetPrinter(printerHandle, 2, buffer, needed, out _))
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
                new DeviceModeValues(
                    deviceMode.Fields,
                    deviceMode.Orientation,
                    deviceMode.PaperSize,
                    deviceMode.DefaultSource,
                    deviceMode.PrintQuality,
                    deviceMode.YResolution),
                media,
                sources);
        }
        finally
        {
            FreeIfSet(buffer);
            if (printerHandle != IntPtr.Zero)
            {
                _ = _interop.ClosePrinter(printerHandle);
            }
        }
    }

    public Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!_isWindows)
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

            if (!_interop.EnumJobs(printerHandle, 0, JobEnumerationLimit, 2, 0, 0, out var needed, out _)
                && _interop.GetLastError() != ErrorInsufficientBuffer)
            {
                ThrowLastError(nameof(WindowsSpoolerInterop.EnumJobs));
            }

            if (needed == 0)
            {
                return Task.FromResult<IReadOnlyList<PrintJobInfo>>([]);
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (!_interop.EnumJobs(printerHandle, 0, JobEnumerationLimit, 2, buffer, needed, out _, out var returned))
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
                _ = _interop.ClosePrinter(printerHandle);
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
            StateReasons = WindowsSpoolerStatusMapper.JobStateReasons(info.Status),
        };
    }

    // EnumJobs addresses jobs by queue position, so there is no single-job lookup.
    public async Task<PrintJobInfo?> GetJobAsync(string queueName, string jobId, CancellationToken cancellationToken)
    {
        if (!_isWindows)
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
        if (!_isWindows)
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

            if (!_interop.SetJob(printerHandle, id, 0, 0, WindowsSpoolerInterop.JobControlCancel))
            {
                var error = _interop.GetLastError();
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
                _ = _interop.ClosePrinter(printerHandle);
            }
        }
    }

    private void OpenForUse(string queueName, out nint printerHandle)
    {
        var defaults = new WindowsSpoolerInterop.PrinterDefaults
        {
            DataType = 0,
            DevMode = 0,
            DesiredAccess = WindowsSpoolerInterop.PrinterAccessUse,
        };

        if (!_interop.OpenPrinter(queueName, out printerHandle, defaults))
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

    // No log entry here on purpose. The exception carries the operation and the message of
    // the operating system, and it reaches the caller unchanged, so a second report of the
    // same failure adds nothing. Reaching a logger from here would mean an instance method,
    // and about twenty helpers above it would have to stop being static for one entry.
    private void ThrowLastError(string operation) =>
        throw new InvalidOperationException(Describe(operation, _interop.GetLastError()));

    // GetPInvokeErrorMessage needs no reflection, so it is safe under native AOT.
    private static string Describe(string operation, int error) =>
        $"{operation} failed with Win32 error {error}: {Marshal.GetPInvokeErrorMessage(error)}";
}
