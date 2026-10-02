using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

// A printer that rasters only outside PdfRenderLimits gets a page rendered at the limit and
// scaled to its own resolution, so the header, the pixels and the media agree.
public class PdfPayloadConverterTests
{
    private const int SyncLength = 4;
    private const int PrinterDpi = 1200;

    [Fact]
    public async Task ConvertAsync_OutsideTheRenderLimits_WritesAHeaderThatAgreesWithTheMedia()
    {
        FakePdfConverter converter = new();
        var context = Context() with { MediaWidthPixels = 2 * PrinterDpi, MediaHeightPixels = PrinterDpi };

        var header = await HeaderAsync(converter, context);

        Assert.Equal(PdfRenderLimits.MaxDpi, converter.RenderedAt);
        Assert.Equal((uint)PrinterDpi, ReadUInt32(header, 276));
        Assert.Equal((uint)PrinterDpi, ReadUInt32(header, 280));
        Assert.Equal(2u * PrinterDpi, ReadUInt32(header, 372));
        Assert.Equal((uint)PrinterDpi, ReadUInt32(header, 376));
        Assert.Equal(144u, ReadUInt32(header, 352));
        Assert.Equal(72u, ReadUInt32(header, 356));
    }

    [Fact]
    public async Task ConvertAsync_ADocumentSizedPageOutsideTheRenderLimits_IsScaledToThePrinterResolution()
    {
        var header = await HeaderAsync(new FakePdfConverter(), Context() with { MediaSizeSource = MediaSizeSource.Document });

        Assert.Equal((uint)PrinterDpi, ReadUInt32(header, 276));
        Assert.Equal(2u * PrinterDpi, ReadUInt32(header, 372));
        Assert.Equal(144u, ReadUInt32(header, 352));
    }

    private static PrintConversionContext Context() =>
        new(PrinterContentTypes.Pdf, PrinterContentTypes.PwgRaster, PrinterDpi, null, "label")
        {
            RasterType = "sgray_8",
            Scaling = PrintScaling.None,
        };

    private static async Task<byte[]> HeaderAsync(FakePdfConverter converter, PrintConversionContext context)
    {
        var documents = await converter.ConvertAsync([], context, TestContext.Current.CancellationToken);
        return Assert.Single(documents)[SyncLength..(SyncLength + 1796)];
    }

    private static uint ReadUInt32(byte[] header, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset, 4));

    // A two by one inch page, rendered white at whatever resolution it is asked for.
    private sealed class FakePdfConverter : PdfPayloadConverter
    {
        public override string Name => "Fake";

        public int RenderedAt { get; private set; }

        protected override async IAsyncEnumerable<RenderedPdfPage> RenderAsync(
            byte[] pdf,
            PrintConversionContext context,
            int dpi,
            RasterColorSpace colorSpace,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RenderedAt = dpi;
            var pixels = new byte[2 * dpi * dpi];
            Array.Fill(pixels, (byte)0xFF);
            await Task.Yield();
            yield return new RenderedPdfPage(pixels, 2 * dpi, dpi, colorSpace, 144, 72);
        }
    }
}
