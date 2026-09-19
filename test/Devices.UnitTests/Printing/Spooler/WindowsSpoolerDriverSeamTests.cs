#nullable enable
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// The Windows spooler driver against a spooler that answers like <c>winspool.drv</c>.
/// </summary>
/// <remarks>
/// What is under test is everything the driver does around the native calls: the two-call
/// buffer protocol, the structure layouts it reads back, the order in which a failed job is
/// torn down, and the Win32 error codes it turns into exceptions or into a plain
/// <c>false</c>. None of that needs Windows, and hardware would not produce most of it.
/// </remarks>
public class WindowsSpoolerDriverSeamTests
{
    private static WindowsSpoolerDriver DriverFor(
        FakeWindowsSpoolerInterop interop,
        FakeWindowsGdiImagePrinter? images = null) =>
        new(interop, images ?? new FakeWindowsGdiImagePrinter(), isWindows: true);

    [Fact]
    public async Task EnumeratePrintersAsync_AsksForTheSizeThenTheData()
    {
        FakeWindowsSpoolerInterop interop = new();
        interop.Queues.Clear();
        interop.Queues.AddRange(["lobby", "warehouse"]);

        var printers = await DriverFor(interop).EnumeratePrintersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["lobby", "warehouse"], printers.Select(printer => printer.Id.Authority));
        Assert.All(printers, printer => Assert.Equal(PrinterScheme.Spooler, printer.Id.Scheme));

        // The size query and the read. A single call would mean a buffer guessed at.
        Assert.Equal(2, interop.Calls.Count(call => call == nameof(IWindowsSpoolerInterop.EnumPrinters)));
    }

    [Fact]
    public async Task EnumeratePrintersAsync_ReportsNothingWhenTheSpoolerHasNoQueue()
    {
        FakeWindowsSpoolerInterop interop = new();
        interop.Queues.Clear();

        var printers = await DriverFor(interop).EnumeratePrintersAsync(TestContext.Current.CancellationToken);

        Assert.Empty(printers);

        // Nothing to read, so the second call must not happen.
        Assert.Equal(1, interop.Calls.Count(call => call == nameof(IWindowsSpoolerInterop.EnumPrinters)));
    }

    [Fact]
    public async Task EnumeratePrintersAsync_AQueueNoIdentifierCanCarry_IsSkippedAndTheRestAreReported()
    {
        // Windows accepts names no identifier can carry, such as the in-box
        // "Generic / Text Only". One of them must not hide the queues beside it.
        FakeWindowsSpoolerInterop interop = new();
        interop.Queues.Clear();
        interop.Queues.AddRange(["lobby", "Generic / Text Only", "warehouse"]);

        var printers = await DriverFor(interop).EnumeratePrintersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["lobby", "warehouse"], printers.Select(printer => printer.Id.Authority));
    }

    [Fact]
    public async Task EnumeratePrintersAsync_AnErrorThatIsNotAShortBuffer_Throws()
    {
        // 1722: RPC_S_SERVER_UNAVAILABLE, a print server that is not answering.
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.EnumPrinters), FailureError = 1722 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DriverFor(interop).EnumeratePrintersAsync(TestContext.Current.CancellationToken));

        Assert.Contains("1722", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWindowsSpoolerInterop.EnumPrinters), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetIdentityAsync_ReadsPrinterInfo2AndClosesTheHandle()
    {
        FakeWindowsSpoolerInterop interop = new()
        {
            PrinterInfo = new WindowsSpoolerInterop.PrinterInfo2
            {
                PrinterName = "lobby",
                Comment = "Lobby LaserJet",
                Location = "Reception",
                PortName = "USB001",
                // PRINTER_ATTRIBUTE_DEFAULT | PRINTER_ATTRIBUTE_SHARED
                Attributes = 0x00000004 | 0x00000008,
            },
        };

        var identity = await DriverFor(interop).GetIdentityAsync("lobby", TestContext.Current.CancellationToken);

        Assert.NotNull(identity);
        Assert.Equal("Lobby LaserJet", identity.Name);
        Assert.Equal("Reception", identity.Location);
        Assert.Equal("USB001", identity.DeviceUri);
        Assert.True(identity.IsDefault);
        Assert.True(identity.IsShared);
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task GetStatusAsync_MapsTheStatusBitsOfTheQueue()
    {
        // PRINTER_STATUS_PAPER_OUT (0x10) | PRINTER_STATUS_ERROR (0x02).
        FakeWindowsSpoolerInterop interop = new()
        {
            PrinterInfo = new WindowsSpoolerInterop.PrinterInfo2 { PrinterName = "lobby", Status = 0x00000012 },
        };

        var status = await DriverFor(interop).GetStatusAsync("lobby", TestContext.Current.CancellationToken);

        Assert.Equal(PrinterId.ForSpooler("lobby"), status.PrinterId);
        Assert.NotEqual(PrinterStatusState.Idle, status.State);
        Assert.NotEmpty(status.StateReasons);
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task GetJobsAsync_ReadsAnArrayOfJobInfo2WithoutMisalignment()
    {
        FakeWindowsSpoolerInterop interop = new();
        interop.Jobs.AddRange([
            new WindowsSpoolerInterop.JobInfo2 { JobId = 7, Document = "first", TotalPages = 3, PagesPrinted = 1, Status = 0x00000010 },
            new WindowsSpoolerInterop.JobInfo2 { JobId = 8, Document = "second", TotalPages = 1, Status = 0x00000001 },
        ]);

        var jobs = await DriverFor(interop).GetJobsAsync("lobby", TestContext.Current.CancellationToken);

        // The second entry is the one that proves the layout: it is read at one struct
        // size past the first, so a wrong offset shows up here and nowhere else.
        Assert.Equal(["7", "8"], jobs.Select(job => job.JobId));
        Assert.Equal("first", jobs[0].JobName);
        Assert.Equal("second", jobs[1].JobName);
        Assert.Equal(3, jobs[0].TotalImpressions);
        Assert.Equal(1, jobs[0].ImpressionsCompleted);
    }

    [Fact]
    public async Task GetJobAsync_PicksOneJobOutOfTheQueue()
    {
        FakeWindowsSpoolerInterop interop = new();
        interop.Jobs.AddRange([
            new WindowsSpoolerInterop.JobInfo2 { JobId = 7, Document = "first" },
            new WindowsSpoolerInterop.JobInfo2 { JobId = 8, Document = "second" },
        ]);

        var job = await DriverFor(interop).GetJobAsync("lobby", "8", TestContext.Current.CancellationToken);

        Assert.NotNull(job);
        Assert.Equal("second", job.JobName);
        Assert.Null(await DriverFor(interop).GetJobAsync("lobby", "99", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelJobAsync_AJobThatLeftTheQueue_IsFalseAndNotAFailure()
    {
        // 87 is ERROR_INVALID_PARAMETER, which SetJob reports for a job that already
        // finished. It is an answer, not a fault, and must not reach the caller as one.
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.SetJob), FailureError = 87 };

        Assert.False(await DriverFor(interop).CancelJobAsync("lobby", "7", TestContext.Current.CancellationToken));
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task CancelJobAsync_AnyOtherError_Throws()
    {
        // 5 is ERROR_ACCESS_DENIED: someone else's job, which the caller must hear about.
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.SetJob), FailureError = 5 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DriverFor(interop).CancelJobAsync("lobby", "7", TestContext.Current.CancellationToken));

        Assert.Contains("5", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task CancelJobAsync_AJobIdThatIsNotANumber_IsFalseWithoutOpeningTheQueue()
    {
        FakeWindowsSpoolerInterop interop = new();

        Assert.False(await DriverFor(interop).CancelJobAsync("lobby", "not-a-job", TestContext.Current.CancellationToken));
        Assert.Empty(interop.Calls);
    }

    [Fact]
    public async Task CancelJobAsync_CancelsTheJobItWasGiven()
    {
        FakeWindowsSpoolerInterop interop = new();

        Assert.True(await DriverFor(interop).CancelJobAsync("lobby", "7", TestContext.Current.CancellationToken));

        Assert.Equal([7], interop.CancelledJobs);
    }

    [Fact]
    public async Task SubmitAsync_SpoolsAPrinterLanguageAsRawBytes()
    {
        FakeWindowsSpoolerInterop interop = new();

        var job = await DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "label" },
            TestContext.Current.CancellationToken);

        Assert.Equal(PrintJobState.Queued, job.State);
        Assert.Equal(PrinterId.ForSpooler("lobby"), job.PrinterId);
        Assert.Equal("^XA^XZ", Encoding.UTF8.GetString(Assert.Single(interop.Written)));

        // Start, write, end, in that order, and the handle closed after.
        Assert.Equal(
            [nameof(IWindowsSpoolerInterop.StartDocPrinter), nameof(IWindowsSpoolerInterop.StartPagePrinter), nameof(IWindowsSpoolerInterop.WritePrinter)],
            interop.Calls.Where(call => call.EndsWith("Printer", StringComparison.Ordinal) && call != nameof(IWindowsSpoolerInterop.OpenPrinter) && call != nameof(IWindowsSpoolerInterop.ClosePrinter) && call != nameof(IWindowsSpoolerInterop.EndPagePrinter) && call != nameof(IWindowsSpoolerInterop.EndDocPrinter)));
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task SubmitAsync_AShortWrite_KeepsGoingUntilEverythingIsSpooled()
    {
        // WritePrinter is allowed to take fewer bytes than it was offered. A driver that
        // ignored the count would spool a truncated label and report success.
        FakeWindowsSpoolerInterop interop = new() { WriteChunk = 2 };
        const string payload = "^XA^FDhello^FS^XZ";

        _ = await DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString(payload, PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(payload, Encoding.UTF8.GetString(Assert.Single(interop.Written)));
        Assert.True(interop.Calls.Count(call => call == nameof(IWindowsSpoolerInterop.WritePrinter)) > 1);
    }

    [Fact]
    public async Task SubmitAsync_AWriteThatMakesNoProgress_FailsInsteadOfLooping()
    {
        FakeWindowsSpoolerInterop interop = new() { WriteNothing = true };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));

        Assert.Contains("0 of", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_AFailedWrite_DeletesTheJobSoNoTruncatedDocumentCommits()
    {
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.WritePrinter), FailureError = 1801 };

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));

        // The started job is removed with JOB_CONTROL_DELETE, the page and the document are
        // closed, and the handle is released. Half a label must not reach the paper.
        Assert.Equal([1], interop.DeletedJobs);
        Assert.Contains(nameof(IWindowsSpoolerInterop.EndPagePrinter), interop.Calls);
        Assert.Contains(nameof(IWindowsSpoolerInterop.EndDocPrinter), interop.Calls);
        Assert.Empty(interop.Written);
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task SubmitAsync_AQueueThatCannotBeOpened_ThrowsAndClosesNothing()
    {
        // 1801 is ERROR_INVALID_PRINTER_NAME.
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.OpenPrinter), FailureError = 1801 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DriverFor(interop).SubmitAsync(
            "nowhere",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));

        Assert.Contains("1801", failure.Message, StringComparison.Ordinal);

        // OpenPrinter leaves the handle undefined on failure, so the driver must not pass
        // whatever it was given to ClosePrinter.
        Assert.DoesNotContain(nameof(IWindowsSpoolerInterop.ClosePrinter), interop.Calls);
    }

    [Fact]
    public async Task SubmitAsync_MoreThanOneCopy_SpoolsOneJobEachAndReportsTheFirst()
    {
        FakeWindowsSpoolerInterop interop = new();

        var job = await DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Copies = 3 },
            TestContext.Current.CancellationToken);

        // A RAW queue never reads dmCopies, so the driver loops instead. The caller is told
        // so, because only the first identifier can be watched.
        Assert.Equal(3, interop.Written.Count);
        Assert.Equal("1", job.JobId);
        Assert.Contains("Copy 1 of 3", job.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitAsync_AnOptionTheJobNamed_ReachesTheDeviceMode()
    {
        FakeWindowsSpoolerInterop interop = new();

        _ = await DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Orientation = PrintOrientation.Landscape },
            TestContext.Current.CancellationToken);

        // DMORIENT_LANDSCAPE, with DM_ORIENTATION set so the driver reads the field at all.
        Assert.NotNull(interop.AcceptedDeviceMode);
        Assert.Equal(2, interop.AcceptedDeviceMode.Value.Orientation);
        Assert.NotEqual(0u, interop.AcceptedDeviceMode.Value.Fields & 0x00000001);
    }

    [Fact]
    public async Task SubmitAsync_ADeviceModeShorterThanDevmodew_IsRefused()
    {
        // Writing the mapped fields back into a buffer this short would overwrite the
        // driver-private tail behind it.
        FakeWindowsSpoolerInterop interop = new() { ShortDeviceModeSize = 8 };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => DriverFor(interop).SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Orientation = PrintOrientation.Landscape },
            TestContext.Current.CancellationToken));

        Assert.Contains("8 byte device mode", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task SubmitAsync_AnImage_GoesThroughGdiAndNotThroughTheRawPath()
    {
        FakeWindowsSpoolerInterop interop = new();
        FakeWindowsGdiImagePrinter images = new() { JobId = 77 };
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

        var job = await DriverFor(interop, images).SubmitAsync(
            "lobby",
            PrinterPayload.FromBytes(png, PrinterContentTypes.Png),
            new PrintOptions { JobName = "photo" },
            TestContext.Current.CancellationToken);

        // An image on a RAW queue prints its own bytes as text, which is why it goes to
        // the driver through GDI instead.
        Assert.Equal("77", job.JobId);
        Assert.Empty(interop.Written);
        var drawn = Assert.Single(images.Jobs);
        Assert.Equal("lobby", drawn.QueueName);
        Assert.Equal("photo", drawn.JobName);
        Assert.Equal(png, Assert.Single(Assert.Single(images.Pages)));
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsTheCapabilityListsOfTheDriver()
    {
        FakeWindowsSpoolerInterop interop = new();
        interop.Capabilities[WindowsSpoolerInterop.DcPaperNames] = new FakeWindowsSpoolerInterop.NameList(["A4", "Letter"], 64);
        interop.Capabilities[WindowsSpoolerInterop.DcPapers] = new short[] { 9, 1 };
        interop.Capabilities[WindowsSpoolerInterop.DcBinNames] = new FakeWindowsSpoolerInterop.NameList(["Tray 1", "Manual"], 24);
        interop.Capabilities[WindowsSpoolerInterop.DcBins] = new short[] { 1, 4 };
        interop.Capabilities[WindowsSpoolerInterop.DcDuplex] = 1;
        interop.Capabilities[WindowsSpoolerInterop.DcColorDevice] = 1;
        interop.Capabilities[WindowsSpoolerInterop.DcEnumResolutions] = new short[] { 600, 0, 600, 0 };

        var configuration = await DriverFor(interop).GetConfigurationAsync("lobby", TestContext.Current.CancellationToken);

        Assert.Equal(["A4", "Letter"], configuration.MediaSizes);
        Assert.Equal(["Tray 1", "Manual"], configuration.MediaSources.Select(source => source.Name));
        Assert.True(configuration.SupportsDuplex);
        Assert.True(configuration.SupportsColor);
        Assert.Contains(600, configuration.SupportedResolutionsDpi);
    }

    [Fact]
    public async Task GetConfigurationAsync_ADriverThatListsNothing_ReportsNothing()
    {
        FakeWindowsSpoolerInterop interop = new();

        var configuration = await DriverFor(interop).GetConfigurationAsync("lobby", TestContext.Current.CancellationToken);

        // A queue with no driver has no media. Inventing a default here would be a guess
        // the caller could not tell from an answer.
        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.MediaSources);
    }

    [Fact]
    public async Task SubmitAsync_ADocument_ConvertsItToPagesAndPrintsOneGdiJob()
    {
        FakeWindowsSpoolerInterop interop = new();
        FakeWindowsGdiImagePrinter images = new() { JobId = 55 };
        RecordingPdfConverter converter = new(pages: 3);
        WindowsSpoolerDriver driver = new(
            interop,
            images,
            isWindows: true,
            new PrintFormatPolicy(null, [converter]));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromBytes(new byte[] { 1, 2, 3 }, PrinterContentTypes.Pdf),
            new PrintOptions { JobName = "report", ResolutionDpi = 600 },
            TestContext.Current.CancellationToken);

        // A document is rendered before any job exists, so a file that cannot be read
        // fails without leaving half of it in the queue. All of it prints as one job.
        Assert.Equal("55", job.JobId);
        Assert.Empty(interop.Written);
        var pages = Assert.Single(images.Pages);
        Assert.Equal(3, pages.Count);

        // The pages carry the resolution they were rendered at: the PNG encoder writes
        // none, and GDI+ would otherwise read them as 96 dpi and print them oversized.
        var drawn = Assert.Single(images.Jobs);
        Assert.Equal(600, drawn.SourceDpi);
        Assert.Equal(PrinterContentTypes.Png, converter.LastTarget);
        Assert.Equal(600, converter.LastDpi);
    }

    [Fact]
    public async Task SubmitAsync_ADocumentPageRange_IsGivenToTheConverterAndNotToTheDriver()
    {
        FakeWindowsSpoolerInterop interop = new();
        RecordingPdfConverter converter = new(pages: 2);
        WindowsSpoolerDriver driver = new(
            interop,
            new FakeWindowsGdiImagePrinter(),
            isWindows: true,
            new PrintFormatPolicy(null, [converter]));

        var job = await driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromBytes(new byte[] { 1 }, PrinterContentTypes.Pdf),
            new PrintOptions { PageRanges = [new PageRange(2, 3)] },
            TestContext.Current.CancellationToken);

        // The converter selects the pages, so this is the one Windows path that honours a
        // page range: a device mode has no field for one.
        Assert.NotNull(converter.LastRanges);
        Assert.Equal(2, converter.LastRanges[0].Lower);
        Assert.DoesNotContain(nameof(PrintOptions.PageRanges), job.DroppedOptions);
    }

    [Fact]
    public async Task SubmitAsync_ADocumentWithNoConverter_FailsBeforeAnythingSpools()
    {
        FakeWindowsSpoolerInterop interop = new();
        FakeWindowsGdiImagePrinter images = new();
        WindowsSpoolerDriver driver = new(interop, images, isWindows: true);

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromBytes(new byte[] { 1, 2, 3 }, PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken));

        Assert.Empty(images.Jobs);
        Assert.Empty(interop.Written);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsTheDefaultsOutOfTheQueueDeviceMode()
    {
        var deviceMode = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsSpoolerInterop.DevMode>());
        try
        {
            // DM_ORIENTATION | DM_PAPERSIZE, with landscape and A4.
            Marshal.StructureToPtr(
                new WindowsSpoolerInterop.DevMode
                {
                    DeviceName = "lobby",
                    FormName = "A4",
                    Fields = 0x00000001 | 0x00000002,
                    Orientation = 2,
                    PaperSize = 9,
                },
                deviceMode,
                false);

            FakeWindowsSpoolerInterop interop = new()
            {
                PrinterInfo = new WindowsSpoolerInterop.PrinterInfo2 { PrinterName = "lobby", DevMode = deviceMode },
            };
            interop.Capabilities[WindowsSpoolerInterop.DcPaperNames] = new FakeWindowsSpoolerInterop.NameList(["Letter", "A4"], 64);
            interop.Capabilities[WindowsSpoolerInterop.DcPapers] = new short[] { 1, 9 };

            var configuration = await DriverFor(interop).GetConfigurationAsync("lobby", TestContext.Current.CancellationToken);

            // Paper 9 is A4, and the name comes from the queue's own list rather than a
            // table of our own: a driver may name its sizes whatever it likes.
            Assert.Equal("A4", configuration.DefaultMediaSize);
            Assert.Equal(PrintOrientation.Landscape, configuration.DefaultOrientation);
        }
        finally
        {
            Marshal.FreeHGlobal(deviceMode);
        }
    }

    [Fact]
    public async Task GetConfigurationAsync_AQueueWithNoDeviceMode_ReportsNoDefaults()
    {
        FakeWindowsSpoolerInterop interop = new()
        {
            PrinterInfo = new WindowsSpoolerInterop.PrinterInfo2 { PrinterName = "lobby", DevMode = IntPtr.Zero },
        };

        var configuration = await DriverFor(interop).GetConfigurationAsync("lobby", TestContext.Current.CancellationToken);

        Assert.Null(configuration.DefaultMediaSize);
        Assert.Null(configuration.DefaultOrientation);
    }

    [Fact]
    public async Task GetIdentityAsync_AQueueThatWillNotAnswer_IsNullAndNotAFailure()
    {
        // Discovery reads identities for every queue it found. One dead print server must
        // not take the whole enumeration down with it.
        FakeWindowsSpoolerInterop interop = new() { FailingCall = nameof(IWindowsSpoolerInterop.GetPrinter), FailureError = 1722 };

        Assert.Null(await DriverFor(interop).GetIdentityAsync("lobby", TestContext.Current.CancellationToken));
        Assert.Equal(0, interop.OpenHandleCount);
    }

    [Fact]
    public async Task GetConfigurationAsync_ADriverThatRefusesTheQuery_ThrowsRatherThanReportNothing()
    {
        // DeviceCapabilities answers -1 for a queue that does not exist, and 0 for a real
        // queue that lists nothing. The two must not read the same: an empty configuration
        // says the printer has no trays, and a caller acts on that.
        FakeWindowsSpoolerInterop interop = new()
        {
            FailingCall = nameof(IWindowsSpoolerInterop.DeviceCapabilities),
            FailureError = 1801,
        };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DriverFor(interop).GetConfigurationAsync("nowhere", TestContext.Current.CancellationToken));

        Assert.Contains(nameof(IWindowsSpoolerInterop.DeviceCapabilities), failure.Message, StringComparison.Ordinal);
        Assert.Contains("1801", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryEntryPoint_ACancelledToken_ThrowsBeforeItReachesTheSpooler()
    {
        FakeWindowsSpoolerInterop interop = new();
        var driver = DriverFor(interop);
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        var cancelled = source.Token;

        // The spooler RPC runs on the caller thread and cannot be interrupted, so the token
        // is read on entry and nowhere else. That makes the entry check the whole of the
        // contract: a call that got past it will finish whatever the caller does next.
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.EnumeratePrintersAsync(cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.GetIdentityAsync("lobby", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.GetStatusAsync("lobby", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.GetConfigurationAsync("lobby", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.GetJobsAsync("lobby", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.GetJobAsync("lobby", "1", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.CancelJobAsync("lobby", "1", cancelled));
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SubmitAsync(
            "lobby",
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            cancelled));

        Assert.Empty(interop.Calls);
    }

    [Fact]
    public void Constructor_RejectsAMissingCollaborator()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new WindowsSpoolerDriver(null!, new FakeWindowsGdiImagePrinter(), true));
        _ = Assert.Throws<ArgumentNullException>(() => new WindowsSpoolerDriver(new FakeWindowsSpoolerInterop(), null!, true));
    }

    [Fact]
    public async Task EveryEntryPoint_ThrowsOffWindowsWhateverTheSpoolerWouldAnswer()
    {
        FakeWindowsSpoolerInterop interop = new();
        WindowsSpoolerDriver driver = new(interop, new FakeWindowsGdiImagePrinter(), isWindows: false);
        var cancellationToken = TestContext.Current.CancellationToken;

        _ = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => driver.EnumeratePrintersAsync(cancellationToken));
        _ = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => driver.GetStatusAsync("lobby", cancellationToken));
        _ = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => driver.GetConfigurationAsync("lobby", cancellationToken));
        _ = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => driver.GetJobsAsync("lobby", cancellationToken));
        _ = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => driver.CancelJobAsync("lobby", "1", cancellationToken));

        // The guard comes first: nothing may reach the spooler on a platform without one.
        Assert.Empty(interop.Calls);
    }
}
