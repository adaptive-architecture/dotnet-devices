#nullable enable
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

/// <summary>
/// What <see cref="PngWriter"/> writes, read back through a decoder written here against
/// RFC 2083 rather than against the writer.
/// </summary>
/// <remarks>
/// A wrong file prints garbage instead of failing, and a decoder that shares the writer's
/// idea of the format agrees with it whatever both do. So the chunks are walked, the
/// checksums recomputed, the zlib stream inflated and the scanlines unfiltered here.
/// </remarks>
public class PngWriterTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Encode_BeginsWithTheSignatureOfEveryPngFile()
    {
        var png = PngWriter.Encode(new byte[4], 2, 2, PngColorType.Grayscale8, 300);

        Assert.Equal(Signature, png.Take(Signature.Length));
    }

    [Fact]
    public void Encode_WritesTheChunksInTheOrderTheSpecificationRequires()
    {
        var png = PngWriter.Encode(new byte[12], 2, 2, PngColorType.Rgb8, 300);

        // RFC 2083 section 5.6: IHDR first, IEND last, and IDAT after any header chunk.
        Assert.Equal<string[]>(["IHDR", "pHYs", "IDAT", "IEND"], [.. Chunks(png).Select(chunk => chunk.Type)]);
    }

    [Fact]
    public void Encode_ChecksumsEveryChunk()
    {
        var png = PngWriter.Encode(Gradient(7, 5, 1), 7, 5, PngColorType.Grayscale8, 300);

        // Chunks() throws on a checksum that does not recompute, so reading it is the test.
        Assert.Equal(4, Chunks(png).Count);
    }

    [Theory]
    [InlineData(PngColorType.Grayscale8, 0, 1)]
    [InlineData(PngColorType.Rgb8, 2, 3)]
    public void Encode_DescribesTheImageInTheHeader(PngColorType colorType, byte expectedColorType, int bytesPerPixel)
    {
        var png = PngWriter.Encode(Gradient(9, 4, bytesPerPixel), 9, 4, colorType, 300);

        var header = Chunk(png, "IHDR");
        Assert.Equal(13, header.Length);
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32BigEndian(header));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
        Assert.Equal(8, header[8]);                 // bit depth
        Assert.Equal(expectedColorType, header[9]);
        Assert.Equal(0, header[10]);                // compression method: deflate
        Assert.Equal(0, header[11]);                // filter method
        Assert.Equal(0, header[12]);                // interlace method: none
    }

    [Fact]
    public void Encode_StatesTheResolutionInPixelsAMetre()
    {
        var png = PngWriter.Encode(new byte[4], 2, 2, PngColorType.Grayscale8, 300);

        // 300 dots an inch is 300/0.0254 = 11811 pixels a metre, the figure every viewer
        // and every print path reads back as 300.
        var physical = Chunk(png, "pHYs");
        Assert.Equal(11811u, BinaryPrimitives.ReadUInt32BigEndian(physical));
        Assert.Equal(11811u, BinaryPrimitives.ReadUInt32BigEndian(physical.AsSpan(4)));
        Assert.Equal(1, physical[8]);               // unit: the metre
    }

    [Fact]
    public void Encode_GreyscalePixels_ComeBackUnchanged()
    {
        var pixels = Gradient(6, 3, 1);

        var decoded = Decode(PngWriter.Encode(pixels, 6, 3, PngColorType.Grayscale8, 300));

        Assert.Equal(6, decoded.Width);
        Assert.Equal(3, decoded.Height);
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void Encode_ColourPixels_KeepTheirOctetsInOrder()
    {
        // Red, green, blue and white in a row: a channel swap would show as a colour
        // change and not as a difference in size.
        byte[] pixels =
        [
            255, 0, 0,   0, 255, 0,
            0, 0, 255,   255, 255, 255,
        ];

        var decoded = Decode(PngWriter.Encode(pixels, 2, 2, PngColorType.Rgb8, 300));

        Assert.Equal(pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(17)]
    public void Encode_AWidthThatIsNotAMultipleOfFour_RoundTrips(int width)
    {
        // A rendering engine pads a scanline to a word boundary; PNG does not, and a writer
        // that assumed padding would shear every image whose width is not a multiple of it.
        var pixels = Gradient(width, 3, 3);

        var decoded = Decode(PngWriter.Encode(pixels, width, 3, PngColorType.Rgb8, 300));

        Assert.Equal(width, decoded.Width);
        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void Encode_ASinglePixel_IsStillAValidFile()
    {
        var decoded = Decode(PngWriter.Encode([0x40], 1, 1, PngColorType.Grayscale8, 150));

        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal<byte[]>([0x40], decoded.Pixels);
    }

    [Fact]
    public void Encode_ARandomImage_RoundTrips()
    {
        // A page of noise is the case run-length thinking gets wrong, and it is the one a
        // photograph on a page looks like.
        Random random = new(1234);
        var pixels = new byte[23 * 19 * 3];
        random.NextBytes(pixels);

        var decoded = Decode(PngWriter.Encode(pixels, 23, 19, PngColorType.Rgb8, 600));

        Assert.Equal(pixels, decoded.Pixels);
    }

    [Fact]
    public void Encode_ATallImage_KeepsItsLinesInOrder()
    {
        // Every line is one value, counting up, so a line written twice or out of order
        // shows as a wrong value and not as a wrong length.
        var pixels = new byte[4 * 200];
        for (var y = 0; y < 200; y++)
        {
            Array.Fill(pixels, (byte)y, y * 4, 4);
        }

        var decoded = Decode(PngWriter.Encode(pixels, 4, 200, PngColorType.Grayscale8, 300));

        Assert.Equal(pixels, decoded.Pixels);
    }

    [Theory]
    [InlineData(0, 1, 300)]
    [InlineData(1, 0, 300)]
    [InlineData(-1, 1, 300)]
    [InlineData(1, 1, 0)]
    public void Encode_RejectsADimensionOrAResolutionThatIsNotPositive(int width, int height, int dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PngWriter.Encode(new byte[1], width, height, PngColorType.Grayscale8, dpi));

    [Fact]
    public void Encode_RejectsABitmapOfTheWrongLength()
    {
        // Three octets a pixel, so a 4 by 2 colour image is 24 and not 8.
        var exception = Assert.Throws<ArgumentException>(
            () => PngWriter.Encode(new byte[8], 4, 2, PngColorType.Rgb8, 300));

        Assert.Equal("pixels", exception.ParamName);
        Assert.Contains("24 octets", exception.Message, StringComparison.Ordinal);
    }

    // A bitmap no run-length encoder can shorten, so the deflate has real work to do.
    private static byte[] Gradient(int width, int height, int bytesPerPixel)
    {
        var pixels = new byte[width * height * bytesPerPixel];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)((i * 37) % 251);
        }

        return pixels;
    }

    private static byte[] Chunk(byte[] png, string type) =>
        Chunks(png).Single(chunk => String.Equals(chunk.Type, type, StringComparison.Ordinal)).Data;

    // RFC 2083 section 5.3: length, type, data, then a CRC-32 over the type and the data.
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        Assert.Equal(Signature, png.Take(Signature.Length));

        List<(string, byte[])> chunks = [];
        var offset = Signature.Length;
        while (offset < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png[(offset + 8)..(offset + 8 + length)];
            var stated = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));

            Assert.Equal(Crc32(png.AsSpan(offset + 4, 4 + length)), stated);
            chunks.Add((type, data));
            offset += 12 + length;
        }

        Assert.Equal(png.Length, offset);
        return chunks;
    }

    // Section 9.2: the inflated stream is every scanline preceded by its filter type. Only
    // filter 0 is undone, because writing another one would be a change this asserts against.
    private static (int Width, int Height, byte[] Pixels) Decode(byte[] png)
    {
        var header = Chunk(png, "IHDR");
        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header);
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        var stride = width * (header[9] == 2 ? 3 : 1);

        using MemoryStream compressed = new(Chunk(png, "IDAT"));
        using ZLibStream inflate = new(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new();
        inflate.CopyTo(raw);

        var scanlines = raw.ToArray();
        Assert.Equal((stride + 1) * height, scanlines.Length);

        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, scanlines[y * (stride + 1)]);
            Array.Copy(scanlines, (y * (stride + 1)) + 1, pixels, y * stride, stride);
        }

        return (width, height, pixels);
    }

    // Section 15.2, written out longhand so it shares nothing with the writer's table.
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var register = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            register ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                register = (register & 1) != 0 ? 0xEDB88320u ^ (register >> 1) : register >> 1;
            }
        }

        return register ^ 0xFFFFFFFFu;
    }
}
