#nullable enable
using System.Linq;
using System.Runtime.InteropServices;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

/// <summary>
/// The GDI image path against a GDI that draws nothing.
/// </summary>
/// <remarks>
/// What is under test is the page loop and the order it releases things in. A page that
/// fails halfway must not leave the document open, the graphics allocated or the temporary
/// file on disk, and none of that is visible from the outside on a real printer: the page
/// either comes out or it does not. <see cref="WindowsGdiImageLayout"/> has its own tests
/// for the arithmetic; these check that the answer reaches GDI+ unchanged.
/// </remarks>
public class WindowsGdiImagePrinterTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static WindowsGdiJob JobFor(
        nint deviceMode = 0,
        int copies = 1,
        PrintOrientation? orientation = null,
        PrintScaling? scaling = null,
        int? sourceDpi = null,
        PrintPlacement? placement = null,
        bool? smoothing = null,
        PrintFitArea fitArea = PrintFitArea.Printable) =>
        new("lobby", ".png", "photo", deviceMode, copies, orientation, scaling, sourceDpi, placement, smoothing, fitArea);

    [Fact]
    public void Print_DrawsOnePageAndReportsTheJobIdOfTheDocument()
    {
        FakeWindowsGdiInterop gdi = new() { JobId = 91 };
        WindowsGdiImagePrinter printer = new(gdi);

        var jobId = printer.Print(JobFor(), Png);

        Assert.Equal(91, jobId);
        Assert.Equal("lobby", gdi.DeviceName);
        _ = Assert.Single(gdi.Drawn);

        // GDI+ on a printer context starts in 1/100 inch. The layout works in device
        // pixels, so a page drawn without this switch overflows by DPI/100.
        Assert.Equal(WindowsGdiInterop.UnitPixel, gdi.PageUnit);
    }

    [Fact]
    public void PrintPages_PutsEveryPageInOneDocument()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(), [Png, Png, Png]);

        // One document, three pages: a PDF rendered to images must print as one job, not
        // as three that a stapler would separate.
        Assert.Equal(1, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.StartDoc)));
        Assert.Equal(1, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.EndDoc)));
        Assert.Equal(3, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.StartPage)));
        Assert.Equal(3, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.EndPage)));
        Assert.Equal(3, gdi.Drawn.Count);
    }

    [Fact]
    public void PrintPages_WritesEachPageToATemporaryFileAndDeletesIt()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(), [Png, Png]);

        // GDI+ decodes from a file, so the bytes have to reach the disk intact.
        Assert.Equal(2, gdi.LoadedBytes.Count);
        Assert.All(gdi.LoadedBytes, bytes => Assert.Equal(Png, bytes));
        Assert.All(gdi.LoadedPaths, path => Assert.EndsWith(".png", path, StringComparison.Ordinal));
        Assert.Equal(2, gdi.LoadedPaths.Distinct(StringComparer.Ordinal).Count());

        // And leave nothing behind.
        Assert.All(gdi.LoadedPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void PrintPages_ReleasesEverythingItOpened()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(), [Png, Png]);

        Assert.Equal(0, gdi.OpenDeviceContexts);
        Assert.Equal(0, gdi.OpenGraphics);
        Assert.Equal(0, gdi.OpenImages);
        Assert.Equal(gdi.StartupCount, gdi.ShutdownCount);
    }

    [Fact]
    public void PrintPages_APageThatFails_AbortsTheDocumentAndLeavesNothingOpen()
    {
        FakeWindowsGdiInterop gdi = new() { FailingCall = nameof(IWindowsGdiInterop.DrawImageRect), GdiplusFailure = 2 };
        WindowsGdiImagePrinter printer = new(gdi);

        var failure = Assert.Throws<InvalidOperationException>(() => printer.PrintPages(JobFor(), [Png]));

        Assert.Contains("GDI+ status 2", failure.Message, StringComparison.Ordinal);

        // AbortDoc and not EndDoc: half a page must not commit to the spooler.
        Assert.Contains(nameof(IWindowsGdiInterop.AbortDoc), gdi.Calls);
        Assert.DoesNotContain(nameof(IWindowsGdiInterop.EndDoc), gdi.Calls);
        Assert.Equal(0, gdi.OpenDeviceContexts);
        Assert.Equal(0, gdi.OpenGraphics);
        Assert.Equal(0, gdi.OpenImages);
        Assert.Equal(gdi.StartupCount, gdi.ShutdownCount);
        Assert.All(gdi.LoadedPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void PrintPages_AFileGdiPlusCannotDecode_SaysSoAndCleansUp()
    {
        FakeWindowsGdiInterop gdi = new() { FailingCall = nameof(IWindowsGdiInterop.LoadImageFromFile), GdiplusFailure = 3 };
        WindowsGdiImagePrinter printer = new(gdi);

        var failure = Assert.Throws<InvalidOperationException>(() => printer.PrintPages(JobFor(), [Png]));

        Assert.Contains("could not decode", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IWindowsGdiInterop.AbortDoc), gdi.Calls);

        // The page was never started, so nothing may end it.
        Assert.DoesNotContain(nameof(IWindowsGdiInterop.StartPage), gdi.Calls);
        Assert.All(gdi.LoadedPaths, path => Assert.False(File.Exists(path)));
    }

    [Fact]
    public void PrintPages_ADeviceContextThatCannotBeMade_ShutsGdiPlusDownAgain()
    {
        // 1801 is ERROR_INVALID_PRINTER_NAME: a queue that went away between the spooler
        // call and the draw.
        FakeWindowsGdiInterop gdi = new() { FailingCall = nameof(IWindowsGdiInterop.CreateDC), FailureError = 1801 };
        WindowsGdiImagePrinter printer = new(gdi);

        var failure = Assert.Throws<InvalidOperationException>(() => printer.PrintPages(JobFor(), [Png]));

        Assert.Contains("1801", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, gdi.ShutdownCount);
        Assert.Equal(0, gdi.OpenDeviceContexts);
    }

    [Fact]
    public void PrintPages_GdiPlusThatWillNotStart_FailsBeforeOpeningAnything()
    {
        FakeWindowsGdiInterop gdi = new() { FailingCall = nameof(IWindowsGdiInterop.Startup), GdiplusFailure = 18 };
        WindowsGdiImagePrinter printer = new(gdi);

        var failure = Assert.Throws<InvalidOperationException>(() => printer.PrintPages(JobFor(), [Png]));

        Assert.Contains("start GDI+ with status 18", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IWindowsGdiInterop.CreateDC), gdi.Calls);
        Assert.Equal(0, gdi.ShutdownCount);
    }

    [Fact]
    public void PrintPages_ADriverThatReportsNoPage_IsRefused()
    {
        FakeWindowsGdiInterop gdi = new() { PrintableWidth = 0, PrintableHeight = 0 };
        WindowsGdiImagePrinter printer = new(gdi);

        var failure = Assert.Throws<InvalidOperationException>(() => printer.PrintPages(JobFor(), [Png]));

        Assert.Contains("unusable page", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IWindowsGdiInterop.StartDoc), gdi.Calls);
    }

    [Fact]
    public void PrintPages_ScalesTheImageFromItsOwnResolutionToTheDeviceResolution()
    {
        // 1240 pixels at 150 dpi is 8.27 inches, which at 300 dpi is 2480 device pixels:
        // exactly the printable width. The page must come out full width and undistorted.
        FakeWindowsGdiInterop gdi = new()
        {
            ImageWidth = 1240,
            ImageHeight = 1754,
            ImageDpiX = 150f,
            ImageDpiY = 150f,
            DpiX = 300,
            DpiY = 300,
            PrintableWidth = 2480,
            PrintableHeight = 3508,
        };
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(), [Png]);

        var drawn = Assert.Single(gdi.Drawn);
        Assert.Equal(2480, drawn.Width);
        Assert.Equal(3508, drawn.Height);
    }

    [Fact]
    public void PrintPages_APageFromAConverter_UsesTheResolutionItWasRenderedAt()
    {
        // The encoder writes no resolution, so the file claims 96 and GDI+ repeats it. The
        // job carries the truth instead, and ignoring it prints the page at the wrong size.
        FakeWindowsGdiInterop gdi = new()
        {
            ImageWidth = 1240,
            ImageHeight = 1754,
            ImageDpiX = 96f,
            ImageDpiY = 96f,
            DpiX = 300,
            DpiY = 300,
        };
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(sourceDpi: 150), [Png]);

        var drawn = Assert.Single(gdi.Drawn);
        Assert.Equal(2480, drawn.Width);
    }

    [Fact]
    public void PrintPages_ARotatedPage_TurnsTheWorldAroundTheMiddleOfTheSheet()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(orientation: PrintOrientation.Landscape), [Png]);

        var angle = Assert.Single(gdi.Rotations);
        Assert.Equal(WindowsGdiImageLayout.RotationDegrees(PrintOrientation.Landscape), angle);

        // Move to the centre, turn, move back. Rotating without that pair spins the page
        // around its top-left corner and throws the image off the sheet.
        Assert.Equal(2, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.TranslateWorldTransform)));
    }

    [Fact]
    public void PrintPages_AnUprightPage_DoesNotTouchTheWorldTransform()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(orientation: PrintOrientation.Portrait), [Png]);

        Assert.Empty(gdi.Rotations);
        Assert.DoesNotContain(nameof(IWindowsGdiInterop.TranslateWorldTransform), gdi.Calls);
    }

    [Fact]
    public void PrintPages_MoreThanOneCopy_WritesTheCountIntoTheDeviceMode()
    {
        var deviceMode = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsSpoolerInterop.DevMode>());
        try
        {
            Marshal.StructureToPtr(new WindowsSpoolerInterop.DevMode { DeviceName = "lobby", FormName = "A4" }, deviceMode, false);
            FakeWindowsGdiInterop gdi = new();
            WindowsGdiImagePrinter printer = new(gdi);

            _ = printer.PrintPages(JobFor(deviceMode, copies: 4), [Png]);

            // One job with dmCopies set, not four jobs: the driver collates for us. The
            // RAW path cannot do this, which is why it loops instead.
            var written = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(deviceMode);
            Assert.Equal(4, written.Copies);
            Assert.NotEqual(0u, written.Fields & 0x00000100);
            Assert.Equal(1, gdi.Calls.Count(call => call == nameof(IWindowsGdiInterop.StartDoc)));
        }
        finally
        {
            Marshal.FreeHGlobal(deviceMode);
        }
    }

    [Fact]
    public void PrintPages_ASingleCopy_LeavesTheDeviceModeAlone()
    {
        var deviceMode = Marshal.AllocHGlobal(Marshal.SizeOf<WindowsSpoolerInterop.DevMode>());
        try
        {
            Marshal.StructureToPtr(new WindowsSpoolerInterop.DevMode { DeviceName = "lobby", FormName = "A4" }, deviceMode, false);
            WindowsGdiImagePrinter printer = new(new FakeWindowsGdiInterop());

            _ = printer.PrintPages(JobFor(deviceMode), [Png]);

            var written = Marshal.PtrToStructure<WindowsSpoolerInterop.DevMode>(deviceMode);
            Assert.Equal(0u, written.Fields & 0x00000100);
        }
        finally
        {
            Marshal.FreeHGlobal(deviceMode);
        }
    }

    [Fact]
    public void PrintPages_FittedToTheSheet_CoversTheMarginTheDriverHolds()
    {
        // A driver with a 50-pixel margin all round: the printable area is smaller than the
        // sheet, and the device context draws from the printable corner.
        FakeWindowsGdiInterop gdi = new()
        {
            PrintableWidth = 2380,
            PrintableHeight = 3408,
            SheetWidth = 2480,
            SheetHeight = 3508,
            OffsetX = 50,
            OffsetY = 50,
            ImageWidth = 2480,
            ImageHeight = 3508,
            ImageDpiX = 300f,
            ImageDpiY = 300f,
        };
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(scaling: PrintScaling.Fit, fitArea: PrintFitArea.Physical), [Png]);

        // Fitted to the whole sheet, the page keeps its size and starts before the printable
        // corner: the strip the driver holds is where the page bleeds off.
        var drawn = Assert.Single(gdi.Drawn);
        Assert.Equal(-50, drawn.X);
        Assert.Equal(-50, drawn.Y);
        Assert.Equal(2480, drawn.Width);
        Assert.Equal(3508, drawn.Height);
    }

    [Fact]
    public void PrintPages_FittedToThePrintableArea_StaysInsideTheMargin()
    {
        FakeWindowsGdiInterop gdi = new()
        {
            PrintableWidth = 2380,
            PrintableHeight = 3408,
            SheetWidth = 2480,
            SheetHeight = 3508,
            OffsetX = 50,
            OffsetY = 50,
            ImageWidth = 2480,
            ImageHeight = 3508,
            ImageDpiX = 300f,
            ImageDpiY = 300f,
        };
        WindowsGdiImagePrinter printer = new(gdi);

        // The default, and what PWG 5100.16 means by fitting to the media.
        _ = printer.PrintPages(JobFor(scaling: PrintScaling.Fit), [Png]);

        var drawn = Assert.Single(gdi.Drawn);
        Assert.True(drawn.X >= 0 && drawn.Y >= 0, $"The page started at {drawn.X},{drawn.Y}, outside the printable area.");
        Assert.True(drawn.Width <= 2380, $"The page is {drawn.Width} wide, which leaves the printable area.");
    }

    [Fact]
    public void PrintPages_AnAnchoredPage_SitsInTheCornerOfThePrintableArea()
    {
        FakeWindowsGdiInterop gdi = new()
        {
            ImageWidth = 1240,
            ImageHeight = 1754,
            ImageDpiX = 300f,
            ImageDpiY = 300f,
            DpiX = 300,
            DpiY = 300,
        };
        WindowsGdiImagePrinter printer = new(gdi);

        PrintPlacement placement = new()
        {
            Anchor = PrintAnchor.TopLeft,
            OffsetX = PrintLength.FromInches(0.1),
            OffsetY = PrintLength.FromInches(0.2),
        };

        _ = printer.PrintPages(JobFor(scaling: PrintScaling.None, placement: placement), [Png]);

        // A tenth of an inch at 300 dots an inch is 30 pixels, and a fifth is 60.
        var drawn = Assert.Single(gdi.Drawn);
        Assert.Equal(30, drawn.X);
        Assert.Equal(60, drawn.Y);
    }

    [Fact]
    public void PrintPages_WithoutSmoothing_TellsGdiToTakeTheNearestPixel()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(smoothing: false), [Png]);

        Assert.Equal(WindowsGdiInterop.InterpolationNearestNeighbor, gdi.InterpolationMode);
        Assert.Equal(WindowsGdiInterop.PixelOffsetHalf, gdi.PixelOffsetMode);
    }

    [Fact]
    public void PrintPages_WithSmoothing_LeavesGdiOnItsOwnDefault()
    {
        FakeWindowsGdiInterop gdi = new();
        WindowsGdiImagePrinter printer = new(gdi);

        _ = printer.PrintPages(JobFor(), [Png]);

        Assert.Null(gdi.InterpolationMode);
    }

    [Fact]
    public void PrintPages_RejectsAJobItCannotPrint()
    {
        WindowsGdiImagePrinter printer = new(new FakeWindowsGdiInterop());

        _ = Assert.Throws<ArgumentNullException>(() => printer.PrintPages(null!, [Png]));
        _ = Assert.Throws<ArgumentNullException>(() => printer.PrintPages(JobFor(), null!));
        _ = Assert.Throws<ArgumentNullException>(() => printer.Print(JobFor(), null!));
        _ = Assert.Throws<ArgumentException>(() => printer.PrintPages(JobFor(), []));
        _ = Assert.Throws<ArgumentNullException>(() => new WindowsGdiImagePrinter(null!));
    }
}
