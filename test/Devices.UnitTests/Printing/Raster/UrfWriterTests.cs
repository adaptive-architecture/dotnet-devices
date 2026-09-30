using System.Buffers.Binary;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Raster;
using AdaptArch.Devices.Rasterization;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

// Read back through UrfReader, which follows what CUPS reads, rather than compared as bytes.
public class UrfWriterTests
{
    private const int FileHeaderLength = 12;
    private const int HeaderLength = 32;

    [Fact]
    public void Constructor_WritesTheFileHeaderWithThePageCount()
    {
        MemoryStream stream = new();
        _ = new UrfWriter(stream, new RasterOptions { TotalPageCount = 3 });

        var document = stream.ToArray();
        Assert.Equal(FileHeaderLength, document.Length);
        Assert.Equal("UNIRAST\0"u8.ToArray(), document[..8]);
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(document.AsSpan(8)));
    }

    [Fact]
    public void WritePage_WritesAHeaderOfThirtyTwoOctets()
    {
        MemoryStream stream = new();
        UrfWriter writer = new(stream, new RasterOptions { ColorSpace = RasterColorSpace.Grayscale8 });
        writer.WritePage([0x40], 1, 1);

        // The file header, the page header, then one line repeat and one run of one.
        Assert.Equal(FileHeaderLength + HeaderLength + 3, stream.Length);
    }

    [Theory]
    [InlineData(RasterColorSpace.Grayscale8, 8, 0)]
    [InlineData(RasterColorSpace.Srgb8, 24, 1)]
    public void WritePage_DescribesTheColorSpace(RasterColorSpace space, int bitsPerPixel, int colorSpace)
    {
        var page = WriteSingle(new RasterOptions { ColorSpace = space }, 2, 1);

        Assert.Equal(bitsPerPixel, page.BitsPerPixel);
        Assert.Equal(colorSpace, page.ColorSpace);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData(DuplexMode.Simplex, 1)]
    [InlineData(DuplexMode.ShortEdge, 2)]
    [InlineData(DuplexMode.LongEdge, 3)]
    public void WritePage_DescribesTheSides(DuplexMode? duplex, int expected) =>
        Assert.Equal(expected, WriteSingle(new RasterOptions { Duplex = duplex }, 1, 1).Duplex);

    [Fact]
    public void WritePage_DescribesTheGeometry()
    {
        var page = WriteSingle(new RasterOptions { ResolutionDpi = 600, ColorSpace = RasterColorSpace.Grayscale8 }, 600, 300);

        Assert.Equal(600, page.Width);
        Assert.Equal(300, page.Height);
        Assert.Equal(600, page.ResolutionDpi);
    }

    [Fact]
    public void WritePage_RoundTripsEveryPage()
    {
        MemoryStream stream = new();
        UrfWriter writer = new(stream, new RasterOptions { ColorSpace = RasterColorSpace.Srgb8, TotalPageCount = 2 });
        byte[] first = [1, 2, 3, 1, 2, 3, 9, 9, 9, 4, 5, 6, 4, 5, 6, 4, 5, 6];
        byte[] second = [.. Enumerable.Repeat((byte)0xFF, 3 * 200 * 2)];
        writer.WritePage(first, 3, 2);
        writer.WritePage(second, 200, 2);

        (var count, var pages) = UrfReader.Read(stream.ToArray());

        Assert.Equal(2, count);
        Assert.Equal(first, pages[0].Pixels);
        Assert.Equal(second, pages[1].Pixels);
    }

    // URF names no transform, so the bitmap of a back side arrives the way the printer said.
    [Fact]
    public void WritePage_RotatesTheBackOfALongEdgeSheetThePrinterReadsRotated()
    {
        MemoryStream stream = new();
        UrfWriter writer = new(stream, new RasterOptions
        {
            ColorSpace = RasterColorSpace.Grayscale8,
            Duplex = DuplexMode.LongEdge,
            SheetBack = RasterSheetBack.Rotated,
        });
        writer.WritePage([1, 2, 3, 4], 2, 2);
        writer.WritePage([1, 2, 3, 4], 2, 2);

        (_, var pages) = UrfReader.Read(stream.ToArray());

        Assert.Equal([1, 2, 3, 4], pages[0].Pixels);
        Assert.Equal([4, 3, 2, 1], pages[1].Pixels);
    }

    [Fact]
    public void Constructor_RefusesAMissingArgument()
    {
        Assert.Throws<ArgumentNullException>(() => new UrfWriter(null!, new RasterOptions()));
        Assert.Throws<ArgumentNullException>(() => new UrfWriter(new MemoryStream(), null!));
    }

    private static UrfReader.UrfPage WriteSingle(RasterOptions options, int width, int height)
    {
        MemoryStream stream = new();
        UrfWriter writer = new(stream, options);
        writer.WritePage(new byte[writer.BytesPerLine(width) * height], width, height);
        return Assert.Single(UrfReader.Read(stream.ToArray()).Pages);
    }
}
