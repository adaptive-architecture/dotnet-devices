using System.Buffers.Binary;
using System.IO.Compression;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using AdaptArch.Devices.Printing.Synthesis;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Synthesis;

public class ImageSynthesisTests
{
    [Fact]
    public void PngReader_ReadsWhatPngWriterWrote()
    {
        byte[] pixels = [255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30];

        var image = PngReader.Decode(PngWriter.Encode(pixels, 2, 2, PngColorType.Rgb8, 300));

        Assert.Equal((2, 2, 3), (image.Width, image.Height, image.Colors));
        Assert.Equal(pixels, image.Color);
        Assert.Null(image.Alpha);
        Assert.Equal(300, image.Dpi!.Value, 0);
    }

    [Fact]
    public void PngReader_APaletteWithTransparency_ExpandsToRgbAndAlpha()
    {
        // Two-bit indexes 0, 1, 2, 1 on one line, and entry 1 is half transparent.
        var png = Png(4, 1, 2, 3, [0, 0b00_01_10_01], palette: [1, 2, 3, 4, 5, 6, 7, 8, 9], transparency: [255, 128]);

        var image = PngReader.Decode(png);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 4, 5, 6], image.Color);
        Assert.Equal([255, 128, 255, 128], image.Alpha);
    }

    [Fact]
    public void PngReader_GreyWithAlphaAndFilters_Unfilters()
    {
        // Two lines of two grey-alpha pixels: the first with Sub, the second with Up.
        var png = Png(2, 2, 8, 4, [1, 10, 255, 5, 0, 2, 1, 0, 1, 0]);

        var image = PngReader.Decode(png);

        Assert.Equal([10, 15, 11, 16], image.Color);
        Assert.Equal([255, 255, 255, 255], image.Alpha);
    }

    [Fact]
    public void PngReader_Interlaced_PutsEachPassWhereItBelongs()
    {
        // A 2x2 grey image: pass 1 holds (0,0), pass 6 holds (1,0), pass 7 holds row 1.
        var png = Png(2, 2, 8, 0, [0, 1, 0, 2, 0, 3, 4], interlaced: true);

        Assert.Equal([1, 2, 3, 4], PngReader.Decode(png).Color);
    }

    [Fact]
    public void PngReader_NotAPng_Throws() =>
        Assert.Throws<InvalidDataException>(() => PngReader.Decode([1, 2, 3]));

    [Fact]
    public void JpegInfo_ReadsTheFrameAndTheDensity()
    {
        var info = JpegInfo.Read(Jpeg(640, 480, 3, dpi: 200));

        Assert.Equal(new JpegInfo(640, 480, 3, 200, false), info);
    }

    [Fact]
    public void JpegInfo_AnAdobeCmykJpeg_IsInverted() =>
        Assert.True(JpegInfo.Read(Jpeg(10, 10, 4, adobe: true)).AdobeInverted);

    [Fact]
    public void JpegInfo_NotAJpeg_Throws() =>
        Assert.Throws<InvalidDataException>(() => JpegInfo.Read([0x89, 0x50, 0x4E, 0x47]));

    [Fact]
    public void ImagePdf_AJpeg_IsCarriedUnchanged()
    {
        var jpeg = Jpeg(96, 48, 3, dpi: 96);

        var pdf = ImagePdf.Write(jpeg, PrinterContentTypes.Jpeg, new SynthesisLayout());

        var text = TextPdfTests.Latin1(pdf);
        Assert.Contains("/Filter/DCTDecode", text, StringComparison.Ordinal);
        Assert.Contains(TextPdfTests.Latin1(jpeg), text, StringComparison.Ordinal);

        // An inch by half an inch at its own size.
        Assert.Contains("/MediaBox[0 0 72 36]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagePdf_OnAMedia_PlacesTheImageAsTheAnchorSays()
    {
        var png = PngWriter.Encode(new byte[72 * 72 * 3], 72, 72, PngColorType.Rgb8, 72);

        var pdf = ImagePdf.Write(png, PrinterContentTypes.Png, new SynthesisLayout
        {
            Media = new MediaDimensions(PrintLength.FromInches(4), PrintLength.FromInches(6)),
            Scaling = PrintScaling.None,
            Placement = new PrintPlacement { Anchor = PrintAnchor.TopLeft, OffsetX = PrintLength.FromInches(0.5) },
        });

        var text = TextPdfTests.Latin1(pdf);
        Assert.Contains("/MediaBox[0 0 288 432]", text, StringComparison.Ordinal);

        // An inch square, half an inch from the left and touching the top.
        Assert.Contains("q 72 0 0 72 36 360 cm /Im0 Do Q", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagePdf_WithAlpha_HasASoftMask()
    {
        var png = Png(1, 1, 8, 6, [0, 1, 2, 3, 4]);

        var text = TextPdfTests.Latin1(ImagePdf.Write(png, PrinterContentTypes.Png, new SynthesisLayout { Smoothing = false }));

        Assert.Contains("/SMask", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/Interpolate", text, StringComparison.Ordinal);
    }

    // A PNG of the given header and raw (filtered) scanlines. The reader checks no CRC.
    internal static byte[] Png(
        int width,
        int height,
        byte depth,
        byte colorType,
        byte[] scanlines,
        byte[] palette = null,
        byte[] transparency = null,
        bool interlaced = false)
    {
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = depth;
        header[9] = colorType;
        header[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(png, "IHDR", header);
        if (palette is not null)
        {
            Chunk(png, "PLTE", palette);
        }

        if (transparency is not null)
        {
            Chunk(png, "tRNS", transparency);
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(scanlines);
        }

        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    // The markers JpegInfo reads, and no scan: a PDF reader would decode the rest.
    internal static byte[] Jpeg(int width, int height, byte components, int dpi = 0, bool adobe = false)
    {
        List<byte> jpeg = [0xFF, 0xD8];
        if (dpi > 0)
        {
            jpeg.AddRange([0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 1, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi, 0, 0]);
        }

        if (adobe)
        {
            jpeg.AddRange([0xFF, 0xEE, 0, 14, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0, 100, 0, 0, 0, 0, 0]);
        }

        jpeg.AddRange([0xFF, 0xC2, 0, (byte)(8 + (components * 3)), 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, components]);
        for (var component = 0; component < components; component++)
        {
            jpeg.AddRange([(byte)(component + 1), 0x11, 0]);
        }

        jpeg.AddRange([0xFF, 0xD9]);
        return [.. jpeg];
    }

    private static void Chunk(MemoryStream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        png.Write(System.Text.Encoding.ASCII.GetBytes(type));
        png.Write(data);
        png.Write(new byte[4]);
    }
}
