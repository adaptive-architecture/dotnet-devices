#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// The converter as the Windows spooler uses it: a document opened once and drawn a page at
/// a time into a device context.
/// </summary>
public class PdfiumDeviceRendererTests
{
    private static readonly IPrintDeviceRenderer Renderer = (IPrintDeviceRenderer)PdfiumPrinting.PdfConverter;

    [Fact]
    public async Task OpenAsync_SelectsThePagesAndReadsTheirSize()
    {
        using var document = await Renderer.OpenAsync(
            TestPdf.WithPages(3),
            Context(new PrintOptions { PageRanges = [new PageRange(2, 3)] }),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, document.PageCount);
        Assert.Equal(new MediaDimensions(PrintLength.FromInches(8.5), PrintLength.FromInches(11)), document.PageSize(0));
    }

    [Fact]
    public async Task OpenAsync_ABrokenFile_FailsBeforeAnythingIsDrawn() =>
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Renderer.OpenAsync([1, 2, 3], Context(null), TestContext.Current.CancellationToken));

    [Fact]
    public async Task OpenAsync_TheWrongPassword_SaysSo()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Renderer.OpenAsync(
            EncryptedPdf.OnePage("secret"),
            Context(new PrintOptions { DocumentPassword = "guess" }),
            TestContext.Current.CancellationToken));

        Assert.Contains("password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_HoldsTheEngineUntilTheDocumentIsDisposed()
    {
        // PDFium is not thread-safe, and a spooled job draws its pages long after it opened
        // the document. Another job must wait for it rather than enter the engine beside it.
        var document = await Renderer.OpenAsync(TestPdf.WithPages(1), Context(null), TestContext.Current.CancellationToken);
        var render = PdfiumDocument.RenderAsync(TestPdf.RedSquare(), new PdfRenderOptions { Dpi = 72 }, TestContext.Current.CancellationToken);

        Assert.NotSame(render, await Task.WhenAny(render, Task.Delay(200, TestContext.Current.CancellationToken)));

        document.Dispose();
        document.Dispose();

        _ = Assert.Single(await render);
    }

    [Fact]
    public async Task OpenAsync_AFailedOpen_ReleasesTheEngine()
    {
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Renderer.OpenAsync([1, 2, 3], Context(null), TestContext.Current.CancellationToken));

        _ = Assert.Single(await PdfiumDocument.RenderAsync(TestPdf.RedSquare(), new PdfRenderOptions { Dpi = 72 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Draw_OffWindows_IsNotSupported()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows has device contexts to draw into.");
        }

        using var document = await Renderer.OpenAsync(TestPdf.WithPages(1), Context(null), TestContext.Current.CancellationToken);

        _ = Assert.Throws<PlatformNotSupportedException>(() => document.Draw(1, 0, new ImageRectangle(0, 0, 100, 100), 0, true));
    }

    // The proof that the output is drawing and not one bitmap: a metafile device context
    // records every GDI call made into it, so the bars of a barcode must arrive as paths or
    // polygons, and no bitmap record may stand in for them.
    [Fact]
    public async Task Draw_OnWindows_RecordsTheBarsAsDrawingRatherThanABitmap()
    {
        // Assert.Skip throws, but the platform analyzer cannot see that; the return is its guard.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows has GDI device contexts.");
            return;
        }

        using var document = await Renderer.OpenAsync(TestPdf.Barcode(), Context(null), TestContext.Current.CancellationToken);
        var records = Metafile.Record(deviceContext => document.Draw(deviceContext, 0, new ImageRectangle(0, 0, 1200, 600), 0, false));

        Assert.Contains(records, Metafile.IsDrawing);
        Assert.DoesNotContain(records, Metafile.IsBitmap);
    }

    private static PrintConversionContext Context(PrintOptions? options) =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.Emf, PrintConversionContext.DefaultDpi, options?.PageRanges, "queue")
        {
            DocumentPassword = options?.DocumentPassword,
        };
}

// wingdi.h: an enhanced metafile device context, and the record types that tell a drawing
// from a bitmap.
internal static partial class Metafile
{
    private static readonly uint[] DrawingRecords =
    [
        59, // EMR_BEGINPATH
        62, // EMR_FILLPATH
        64, // EMR_STROKEPATH
        84, // EMR_EXTTEXTOUTW
        85, // EMR_POLYBEZIER16
        86, // EMR_POLYGON16
        87, // EMR_POLYLINE16
        91, // EMR_POLYPOLYGON16
    ];

    private static readonly uint[] BitmapRecords =
    [
        76, // EMR_BITBLT
        77, // EMR_STRETCHBLT
        80, // EMR_SETDIBITSTODEVICE
        81, // EMR_STRETCHDIBITS
    ];

    internal static bool IsDrawing(uint record) => DrawingRecords.Contains(record);

    internal static bool IsBitmap(uint record) => BitmapRecords.Contains(record);

    [SupportedOSPlatform("windows")]
    internal static List<uint> Record(Action<nint> draw)
    {
        var deviceContext = CreateEnhMetaFile(0, null, 0, null);
        Assert.NotEqual(0, deviceContext);
        draw(deviceContext);
        var metafile = CloseEnhMetaFile(deviceContext);
        try
        {
            var bytes = new byte[GetEnhMetaFileBits(metafile, 0, null)];
            _ = GetEnhMetaFileBits(metafile, (uint)bytes.Length, bytes);

            List<uint> records = [];
            for (var offset = 0; offset + 8 <= bytes.Length;)
            {
                records.Add(BitConverter.ToUInt32(bytes, offset));
                offset += (int)BitConverter.ToUInt32(bytes, offset + 4);
            }

            return records;
        }
        finally
        {
            _ = DeleteEnhMetaFile(metafile);
        }
    }

    [LibraryImport("gdi32.dll", EntryPoint = "CreateEnhMetaFileW", StringMarshalling = StringMarshalling.Utf16)]
    [SupportedOSPlatform("windows")]
    private static partial nint CreateEnhMetaFile(nint reference, string? fileName, nint bounds, string? description);

    [LibraryImport("gdi32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial nint CloseEnhMetaFile(nint deviceContext);

    [LibraryImport("gdi32.dll")]
    [SupportedOSPlatform("windows")]
    private static partial uint GetEnhMetaFileBits(nint metafile, uint size, [Out] byte[]? bytes);

    [LibraryImport("gdi32.dll")]
    [SupportedOSPlatform("windows")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteEnhMetaFile(nint metafile);
}
