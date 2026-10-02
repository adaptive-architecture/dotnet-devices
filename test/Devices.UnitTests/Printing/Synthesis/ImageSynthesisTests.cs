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
    public void PngReader_AHeaderClaimingAHugeImage_IsRefusedBeforeAnythingIsAllocated()
    {
        // A hundred bytes declaring 20000 by 20000 RGBA would otherwise allocate 1.6 GB.
        var png = Png(20000, 20000, 8, 6, [0, 0, 0, 0, 0]);

        var error = Assert.Throws<InvalidDataException>(() => PngReader.Decode(png));

        Assert.Contains("20000 by 20000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PngReader_ImageDataShorterThanTheHeaderSays_Throws()
    {
        // Two grey lines declared, one line of data given.
        var png = Png(2, 2, 8, 0, [0, 1, 2]);

        var error = Assert.Throws<InvalidDataException>(() => PngReader.Decode(png));

        Assert.Contains("before its last line", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PngReader_AChunkLongerThanTheFile_IsRefusedByName()
    {
        var png = Png(1, 1, 8, 0, [0, 7]);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(8), 0x7FFFFFF0);

        var error = Assert.Throws<InvalidDataException>(() => PngReader.Decode(png));

        Assert.Contains("'IHDR'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PngReader_AShortHeaderChunk_IsRefusedByName()
    {
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", new byte[8]);

        var error = Assert.Throws<InvalidDataException>(() => PngReader.Decode(png.ToArray()));

        Assert.Contains("header chunk is 8 bytes", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PngReader_AZeroOrOverflowingDimension_IsRefused(int width) =>
        Assert.Throws<InvalidDataException>(() => PngReader.Decode(Png(width, 1, 8, 0, [0, 1])));

    [Fact]
    public void PngReader_APhysChunkOfZero_LeavesTheResolutionUnknown()
    {
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8;
        Chunk(png, "IHDR", header);
        Chunk(png, "pHYs", [0, 0, 0, 0, 0, 0, 0, 0, 1]);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write([0, 9]);
        }

        Chunk(png, "IDAT", compressed.ToArray());

        Assert.Null(PngReader.Decode(png.ToArray()).Dpi);
    }

    [Fact]
    public void PngReader_SixteenBitRgbWithAColourKey_KeepsTheHighOctetAndMasksTheKey()
    {
        // Two pixels: the first is the key (0x1234, 0x5678, 0x9ABC), the second is not.
        byte[] line = [0, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0x12, 0x34, 0x56, 0x78, 0x00, 0x01];
        var png = Png(2, 1, 16, 2, line, transparency: [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC]);

        var image = PngReader.Decode(png);

        Assert.Equal([0x12, 0x56, 0x9A, 0x12, 0x56, 0x00], image.Color);
        Assert.Equal([0, 255], image.Alpha);
    }

    [Fact]
    public void PngReader_AverageAndPaethFilters_Unfilter()
    {
        // Two grey lines of three pixels: Average over (left + up) / 2, then Paeth.
        var png = Png(3, 2, 8, 0, [3, 10, 10, 10, 4, 0, 0, 0]);

        var image = PngReader.Decode(png);

        // Average: 10, then 10 + 10 / 2 = 15, then 10 + 15 / 2 = 17. Paeth: up wins on each
        // pixel, so the line repeats.
        Assert.Equal([10, 15, 17, 10, 15, 17], image.Color);
    }

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
    public void JpegInfo_ACameraJpegWithExifAndNoJfif_ReadsItsDensityAndOrientation()
    {
        // A phone photo: 300 dots a centimetre would be a 4000 pixel page at 96 dpi otherwise.
        var info = JpegInfo.Read(Jpeg(4000, 3000, 3, exif: (Resolution: 72, Unit: 3, Orientation: 6)));

        Assert.Equal(72 * 2.54, info.Dpi!.Value, 6);
        Assert.Equal(6, info.Orientation);
    }

    [Fact]
    public void JpegInfo_JfifDensityWinsOverExif() =>
        Assert.Equal(200, JpegInfo.Read(Jpeg(10, 10, 3, dpi: 200, exif: (Resolution: 96, Unit: 2, Orientation: 1))).Dpi);

    [Theory]
    [InlineData(0xC3)]
    [InlineData(0xC9)]
    [InlineData(0xCB)]
    public void JpegInfo_ALosslessOrArithmeticFrame_IsRefusedByMarker(int marker)
    {
        var error = Assert.Throws<NotSupportedException>(() => JpegInfo.Read(Jpeg(10, 10, 3, sof: (byte)marker)));

        Assert.Contains($"SOF{marker - 0xC0}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void JpegInfo_TwelveBitSamples_AreRefused()
    {
        var error = Assert.Throws<NotSupportedException>(() => JpegInfo.Read(Jpeg(10, 10, 3, precision: 12)));

        Assert.Contains("12-bit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ImagePdf_AJpegTurnedByItsExifOrientation_TakesTheTransposedPageAndIsDrawnTurned()
    {
        // 96 by 48 pixels at 96 dpi, to be shown turned a quarter clockwise: the page is half
        // an inch wide and an inch tall, and the image is drawn with its axes swapped.
        var jpeg = Jpeg(96, 48, 3, dpi: 96, exif: (Resolution: 96, Unit: 2, Orientation: 6));

        var text = TextPdfTests.Latin1(ImagePdf.Write(jpeg, PrinterContentTypes.Jpeg, new SynthesisLayout()));

        Assert.Contains("/MediaBox[0 0 36 72]", text, StringComparison.Ordinal);
        Assert.Contains("q 0 -72 36 0 0 72 cm /Im0 Do Q", text, StringComparison.Ordinal);
    }

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
    internal static byte[] Jpeg(
        int width,
        int height,
        byte components,
        int dpi = 0,
        bool adobe = false,
        byte sof = 0xC2,
        byte precision = 8,
        (int Resolution, int Unit, int Orientation)? exif = null)
    {
        List<byte> jpeg = [0xFF, 0xD8];
        if (dpi > 0)
        {
            jpeg.AddRange([0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 1, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi, 0, 0]);
        }

        if (exif is (var resolution, var unit, var orientation))
        {
            // APP1: "Exif\0\0", then a little-endian TIFF with one directory of three
            // entries and the rational the resolution points at.
            List<byte> tiff = [(byte)'I', (byte)'I', 0x2A, 0, 8, 0, 0, 0, 3, 0];
            tiff.AddRange([0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0]);
            tiff.AddRange([0x1A, 0x01, 5, 0, 1, 0, 0, 0, 50, 0, 0, 0]);
            tiff.AddRange([0x28, 0x01, 3, 0, 1, 0, 0, 0, (byte)unit, 0, 0, 0]);
            tiff.AddRange([0, 0, 0, 0]);
            tiff.AddRange([(byte)resolution, (byte)(resolution >> 8), 0, 0, 1, 0, 0, 0]);
            var length = 2 + 6 + tiff.Count;
            jpeg.AddRange([0xFF, 0xE1, (byte)(length >> 8), (byte)length, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0]);
            jpeg.AddRange(tiff);
        }

        if (adobe)
        {
            jpeg.AddRange([0xFF, 0xEE, 0, 14, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0, 100, 0, 0, 0, 0, 0]);
        }

        jpeg.AddRange([0xFF, sof, 0, (byte)(8 + (components * 3)), precision, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, components]);
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
