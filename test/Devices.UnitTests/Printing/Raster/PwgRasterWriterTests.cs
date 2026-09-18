using System.Buffers.Binary;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

// A wrong stream prints garbage instead of failing, so the bitmap is always read back
// through a decoder written here rather than compared as opaque bytes.
public class PwgRasterWriterTests
{
    private const int HeaderLength = 1796;
    private const int SyncLength = 4;

    [Fact]
    public void Constructor_WritesTheSynchronizationWord()
    {
        MemoryStream stream = new();
        _ = new PwgRasterWriter(stream, new PwgRasterOptions());

        Assert.Equal("RaS2"u8.ToArray(), stream.ToArray());
    }

    [Fact]
    public void WritePage_WritesAHeaderOfExactlySeventeenHundredAndNinetySixOctets()
    {
        var document = WriteGray(1, 1, [0x40]);

        // The sync word, the header, then the bitmap: one line repeat and one run of one.
        Assert.Equal(SyncLength + HeaderLength + 3, document.Length);
    }

    [Theory]
    [InlineData(PwgRasterColorSpace.Grayscale8, 18u, 8u, 1u)]
    [InlineData(PwgRasterColorSpace.Srgb8, 19u, 24u, 3u)]
    public void WritePage_DescribesTheColorSpace(PwgRasterColorSpace space, uint colorSpace, uint bitsPerPixel, uint colors)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = space });
        const int width = 2;
        writer.WritePage(new byte[writer.BytesPerLine(width)], width, 1);

        var header = Header(stream.ToArray());
        Assert.Equal(colorSpace, ReadUInt32(header, 400));
        Assert.Equal(8u, ReadUInt32(header, 384));
        Assert.Equal(bitsPerPixel, ReadUInt32(header, 388));
        Assert.Equal(colors, ReadUInt32(header, 420));
        Assert.Equal(0u, ReadUInt32(header, 396));
    }

    [Fact]
    public void WritePage_DescribesTheGeometry()
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions
        {
            ResolutionDpi = 300,
            ColorSpace = PwgRasterColorSpace.Grayscale8,
            TotalPageCount = 4,
            Copies = 2,
        });

        writer.WritePage(new byte[600 * 300], 600, 300);

        var header = Header(stream.ToArray());
        Assert.Equal(600u, ReadUInt32(header, 372));
        Assert.Equal(300u, ReadUInt32(header, 376));
        Assert.Equal(600u, ReadUInt32(header, 392));
        Assert.Equal(300u, ReadUInt32(header, 276));
        Assert.Equal(300u, ReadUInt32(header, 280));
        Assert.Equal(4u, ReadUInt32(header, 452));
        Assert.Equal(2u, ReadUInt32(header, 340));

        // 600 pixels at 300 dots an inch is two inches, which is 144 points.
        Assert.Equal(144u, ReadUInt32(header, 352));
        Assert.Equal(72u, ReadUInt32(header, 356));

        // The transforms are 1 for a page the printer reads as it is.
        Assert.Equal(1, ReadInt32(header, 456));
        Assert.Equal(1, ReadInt32(header, 460));
    }

    [Theory]
    [InlineData(null, 0u, 0u)]
    [InlineData(DuplexMode.Simplex, 0u, 0u)]
    [InlineData(DuplexMode.LongEdge, 1u, 0u)]
    [InlineData(DuplexMode.ShortEdge, 1u, 1u)]
    public void WritePage_DescribesTheDuplexMode(DuplexMode? duplex, uint expectedDuplex, uint expectedTumble)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = PwgRasterColorSpace.Grayscale8, Duplex = duplex });
        writer.WritePage([0x00], 1, 1);

        var header = Header(stream.ToArray());
        Assert.Equal(expectedDuplex, ReadUInt32(header, 272));
        Assert.Equal(expectedTumble, ReadUInt32(header, 368));
    }

    // PWG 5102.4 Table 9. A front side is always 1 and 1; only a duplex back side changes.
    [Theory]
    [InlineData(DuplexMode.LongEdge, PwgRasterSheetBack.Normal, 1, 1)]
    [InlineData(DuplexMode.LongEdge, PwgRasterSheetBack.ManualTumble, 1, 1)]
    [InlineData(DuplexMode.LongEdge, PwgRasterSheetBack.Flipped, 1, -1)]
    [InlineData(DuplexMode.LongEdge, PwgRasterSheetBack.Rotated, -1, -1)]
    [InlineData(DuplexMode.ShortEdge, PwgRasterSheetBack.Normal, 1, 1)]
    [InlineData(DuplexMode.ShortEdge, PwgRasterSheetBack.ManualTumble, -1, -1)]
    [InlineData(DuplexMode.ShortEdge, PwgRasterSheetBack.Flipped, -1, 1)]
    [InlineData(DuplexMode.ShortEdge, PwgRasterSheetBack.Rotated, 1, 1)]
    public void WritePage_TransformsTheBackOfADuplexSheet(DuplexMode duplex, PwgRasterSheetBack sheetBack, int crossFeed, int feed)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions
        {
            ColorSpace = PwgRasterColorSpace.Grayscale8,
            Duplex = duplex,
            SheetBack = sheetBack,
        });

        writer.WritePage([0x00], 1, 1);
        writer.WritePage([0x00], 1, 1);

        var document = stream.ToArray();
        var front = document[SyncLength..(SyncLength + HeaderLength)];
        var back = document[(SyncLength + HeaderLength + 3)..(SyncLength + (2 * HeaderLength) + 3)];

        Assert.Equal(1, ReadInt32(front, 456));
        Assert.Equal(1, ReadInt32(front, 460));
        Assert.Equal(crossFeed, ReadInt32(back, 456));
        Assert.Equal(feed, ReadInt32(back, 460));
    }

    [Fact]
    public void WritePage_LeavesEveryPageUntransformedWhenTheJobIsOneSided()
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions
        {
            ColorSpace = PwgRasterColorSpace.Grayscale8,
            Duplex = DuplexMode.Simplex,
            SheetBack = PwgRasterSheetBack.Rotated,
        });

        writer.WritePage([0x00], 1, 1);
        writer.WritePage([0x00], 1, 1);

        var back = stream.ToArray()[(SyncLength + HeaderLength + 3)..(SyncLength + (2 * HeaderLength) + 3)];

        Assert.Equal(1, ReadInt32(back, 456));
        Assert.Equal(1, ReadInt32(back, 460));
    }

    [Fact]
    public void WritePage_NamesTheMediaWhenTheOptionsDo()
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions
        {
            ColorSpace = PwgRasterColorSpace.Grayscale8,
            MediaName = "iso_a4_210x297mm",
        });

        writer.WritePage([0x00], 1, 1);

        var header = Header(stream.ToArray());
        Assert.Equal("iso_a4_210x297mm", ReadCString(header, 1732));
        Assert.Equal("PwgRaster", ReadCString(header, 0));
    }

    // PWG 5102.4 section 4.4.1. The specification works this exact bitmap through, so a
    // decoder that agrees with it and an encoder that produces it are both pinned here.
    [Fact]
    public void WritePage_EncodesTheSampleOfTheSpecification()
    {
        // The sample is 1-bit, which this writer does not produce, so it is read as the
        // three octets a line that the specification counts: the run structure is the same.
        byte[] lines =
        [
            0x8F, 0x78, 0xF7,
            0x76, 0x77, 0x67,
            0x77, 0x77, 0x77,
            0x77, 0x77, 0x77,
            0x77, 0x77, 0x77,
            0x77, 0x77, 0x77,
            0x8E, 0x38, 0xE3,
            0xFF, 0xFF, 0xFF,
        ];

        var document = WriteGray(3, 8, lines);
        var bitmap = document.AsSpan(SyncLength + HeaderLength).ToArray();

        byte[] expected =
        [
            0x00, 0xFE, 0x8F, 0x78, 0xF7,
            0x00, 0xFE, 0x76, 0x77, 0x67,
            0x03, 0x02, 0x77,
            0x00, 0xFE, 0x8E, 0x38, 0xE3,
            0x00, 0x02, 0xFF,
        ];

        Assert.Equal(expected, bitmap);
        Assert.Equal(21, bitmap.Length);
    }

    [Fact]
    public void WritePage_EncodesARunOfExactlyOneHundredAndTwentyEight()
    {
        var line = Enumerable.Repeat((byte)0xAB, 128).ToArray();
        var bitmap = BitmapOf(WriteGray(128, 1, line));

        // One line, then "count - 1" for 128 repeats, then the colour once.
        Assert.Equal<byte[]>([0x00, 0x7F, 0xAB], bitmap);
    }

    [Fact]
    public void WritePage_SplitsARunOfOneHundredAndTwentyNine()
    {
        var line = Enumerable.Repeat((byte)0xAB, 129).ToArray();
        var bitmap = BitmapOf(WriteGray(129, 1, line));

        // 128 repeats, then a lone colour, which is a repeat of one and not a literal.
        Assert.Equal<byte[]>([0x00, 0x7F, 0xAB, 0x00, 0xAB], bitmap);
    }

    [Fact]
    public void WritePage_EncodesALoneColorAsARepeatOfOne()
    {
        var bitmap = BitmapOf(WriteGray(1, 1, [0x5A]));

        Assert.Equal<byte[]>([0x00, 0x00, 0x5A], bitmap);
    }

    [Fact]
    public void WritePage_EncodesALiteralRunOfExactlyOneHundredAndTwentyEight()
    {
        var line = Enumerable.Range(0, 128).Select(value => (byte)value).ToArray();
        var bitmap = BitmapOf(WriteGray(128, 1, line));

        Assert.Equal(0x00, bitmap[0]);
        Assert.Equal(257 - 128, bitmap[1]);
        Assert.Equal(line, bitmap[2..]);
    }

    [Fact]
    public void WritePage_RepeatsUpToTwoHundredAndFiftySixEqualLines()
    {
        var pixels = Enumerable.Repeat((byte)0x11, 257).ToArray();
        var bitmap = BitmapOf(WriteGray(1, 257, pixels));

        // 256 lines in one repeat, then the 257th on its own.
        Assert.Equal<byte[]>([0xFF, 0x00, 0x11, 0x00, 0x00, 0x11], bitmap);
    }

    [Fact]
    public void WritePage_KeepsEveryPixelOfAColorPage()
    {
        byte[] pixels =
        [
            0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00,
            0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF,
        ];

        var document = WriteColor(2, 2, pixels);

        Assert.Equal(pixels, Decode(BitmapOf(document), 6, 3));
    }

    [Fact]
    public void WritePage_RoundTripsAPageThatMixesRunsAndLiterals()
    {
        var random = new Random(1234);
        var pixels = new byte[64 * 40];
        for (var i = 0; i < pixels.Length; i++)
        {
            // Long runs and short bursts of noise in the same page.
            pixels[i] = (byte)(random.Next(0, 3) == 0 ? random.Next(0, 256) : 0x20);
        }

        var document = WriteGray(64, 40, pixels);

        Assert.Equal(pixels, Decode(BitmapOf(document), 64, 1));
    }

    [Fact]
    public void WritePage_WritesEveryPageOfADocumentIntoOneStream()
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = PwgRasterColorSpace.Grayscale8, TotalPageCount = 2 });
        writer.WritePage([0x01], 1, 1);
        writer.WritePage([0x02], 1, 1);

        var document = stream.ToArray();

        Assert.Equal(SyncLength + (2 * (HeaderLength + 3)), document.Length);

        // Each page is a header and then its own bitmap, so the second header sits between
        // the two bitmaps rather than after both.
        Assert.Equal<byte[]>([0x00, 0x00, 0x01], document[(SyncLength + HeaderLength)..(SyncLength + HeaderLength + 3)]);
        Assert.Equal<byte[]>([0x00, 0x00, 0x02], document[^3..]);
    }

    [Fact]
    public void WritePage_RejectsABitmapThatIsNotTheSizeTheDimensionsGive()
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = PwgRasterColorSpace.Grayscale8 });

        var exception = Assert.Throws<ArgumentException>(() => writer.WritePage(new byte[5], 2, 2));

        Assert.Contains("4 octets", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void WritePage_RejectsAPageWithNoArea(int width, int height)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions());

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => writer.WritePage([], width, height));
    }

    [Fact]
    public void Constructor_RejectsAResolutionThatIsNotPositive()
    {
        MemoryStream stream = new();

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new PwgRasterWriter(stream, new PwgRasterOptions { ResolutionDpi = 0 }));
    }

    private static byte[] WriteGray(int width, int height, byte[] pixels)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = PwgRasterColorSpace.Grayscale8 });
        writer.WritePage(pixels, width, height);
        return stream.ToArray();
    }

    private static byte[] WriteColor(int width, int height, byte[] pixels)
    {
        MemoryStream stream = new();
        PwgRasterWriter writer = new(stream, new PwgRasterOptions { ColorSpace = PwgRasterColorSpace.Srgb8 });
        writer.WritePage(pixels, width, height);
        return stream.ToArray();
    }

    private static byte[] BitmapOf(byte[] document) => document[(SyncLength + HeaderLength)..];

    private static byte[] Header(byte[] document) => document[SyncLength..(SyncLength + HeaderLength)];

    private static uint ReadUInt32(byte[] header, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset, sizeof(uint)));

    private static int ReadInt32(byte[] header, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(offset, sizeof(int)));

    private static string ReadCString(byte[] header, int offset)
    {
        var field = header.AsSpan(offset, 64);
        var end = field.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(field[..(end < 0 ? field.Length : end)]);
    }

    // The reader of PWG 5102.4 section 4.4, written against the specification and not
    // against the writer, so the two have to agree for a test to pass.
    private static byte[] Decode(byte[] bitmap, int bytesPerLine, int bytesPerPixel)
    {
        List<byte> output = [];
        var index = 0;
        while (index < bitmap.Length)
        {
            var lineRepeat = bitmap[index++] + 1;
            List<byte> line = [];
            while (line.Count < bytesPerLine)
            {
                var control = bitmap[index++];
                if (control <= 127)
                {
                    var count = control + 1;
                    for (var i = 0; i < count; i++)
                    {
                        line.AddRange(bitmap.AsSpan(index, bytesPerPixel).ToArray());
                    }

                    index += bytesPerPixel;
                }
                else
                {
                    var count = 257 - control;
                    line.AddRange(bitmap.AsSpan(index, count * bytesPerPixel).ToArray());
                    index += count * bytesPerPixel;
                }
            }

            Assert.Equal(bytesPerLine, line.Count);
            for (var i = 0; i < lineRepeat; i++)
            {
                output.AddRange(line);
            }
        }

        return [.. output];
    }
}
