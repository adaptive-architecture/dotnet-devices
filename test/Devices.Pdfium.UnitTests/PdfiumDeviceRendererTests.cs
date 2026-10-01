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
    // records every GDI call made into it, so the bars of a barcode must arrive as paths,
    // polygons or solid fills, and no record may carry pixels.
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

        var types = String.Join(", ", records.Select(record => record.Type));
        Assert.True(records.Any(Metafile.IsDrawing), $"No drawing among the records {types}.");
        Assert.False(records.Any(record => record.CarriesPixels), $"A bitmap among the records {types}.");
    }

    private static PrintConversionContext Context(PrintOptions? options) =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.Emf, PrintConversionContext.DefaultDpi, options?.PageRanges, "queue")
        {
            DocumentPassword = options?.DocumentPassword,
        };
}

// One record of an enhanced metafile: its EMR_* type, and whether it carries pixels.
internal readonly record struct MetafileRecord(uint Type, bool CarriesPixels);

// wingdi.h: an enhanced metafile device context, and the record types that tell a drawing
// from a bitmap.
internal static partial class Metafile
{
    // Paths, polygons, rectangles, regions and text. A solid fill is a blit with no source,
    // which is why the blits are judged by what they carry instead of listed here.
    private static readonly uint[] DrawingRecords =
    [
        2, 3, 4, 5, 6, 7, 8, // EMR_POLYBEZIER to EMR_POLYPOLYGON
        42, 43, 44, // EMR_ELLIPSE, EMR_RECTANGLE, EMR_ROUNDRECT
        59, 60, 61, 62, 63, 64, // EMR_BEGINPATH to EMR_STROKEPATH
        71, // EMR_FILLRGN
        83, 84, // EMR_EXTTEXTOUTA, EMR_EXTTEXTOUTW
        85, 86, 87, 88, 89, 90, 91, 92, // EMR_POLYBEZIER16 to EMR_POLYDRAW16
    ];

    // EMR_BITBLT, EMR_STRETCHBLT, EMR_ALPHABLEND and EMR_TRANSPARENTBLT share a layout up to
    // cbBitsSrc, the size of the source pixels, which is zero for a pattern or solid fill.
    private static readonly uint[] Blits = [76, 77, 114, 116];
    private const int SourceBitsSizeOffset = 96;

    // EMR_SETDIBITSTODEVICE and EMR_STRETCHDIBITS exist only to carry pixels.
    private static readonly uint[] PixelRecords = [80, 81];

    internal static bool IsDrawing(MetafileRecord record) =>
        DrawingRecords.Contains(record.Type) || (Blits.Contains(record.Type) && !record.CarriesPixels);

    [SupportedOSPlatform("windows")]
    internal static List<MetafileRecord> Record(Action<nint> draw)
    {
        var deviceContext = CreateEnhMetaFile(0, null, 0, null);
        Assert.NotEqual(0, deviceContext);
        draw(deviceContext);
        var metafile = CloseEnhMetaFile(deviceContext);
        try
        {
            var bytes = new byte[GetEnhMetaFileBits(metafile, 0, null)];
            _ = GetEnhMetaFileBits(metafile, (uint)bytes.Length, bytes);

            List<MetafileRecord> records = [];
            for (var offset = 0; offset + 8 <= bytes.Length;)
            {
                var type = BitConverter.ToUInt32(bytes, offset);
                var size = (int)BitConverter.ToUInt32(bytes, offset + 4);
                var pixels = PixelRecords.Contains(type)
                    || (Blits.Contains(type) && size >= SourceBitsSizeOffset + 4 && BitConverter.ToUInt32(bytes, offset + SourceBitsSizeOffset) > 0);
                records.Add(new MetafileRecord(type, pixels));
                offset += size;
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
