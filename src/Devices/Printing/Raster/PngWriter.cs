using System.Buffers.Binary;
using System.IO.Compression;

namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Writes a PNG image, the format the Windows spooler asks a converter for.
/// </summary>
/// <remarks>
/// RFC 2083, in the subset a rendered print page needs: one non-interlaced 8-bit image,
/// greyscale or truecolour, carried in a single <c>IDAT</c> chunk. No palette, no alpha
/// channel, and no ancillary chunk but <c>pHYs</c>.
/// <para>
/// It lives beside <see cref="PwgRasterWriter"/> and is public for the same reason: the
/// Windows spooler asks a converter for <see cref="PrinterContentTypes.Png"/> by name, so an
/// application that brings its own rasterizer needs an encoder as well as a raster writer,
/// and had to write one. Neither costs this package a dependency — the deflate comes from
/// <see cref="ZLibStream"/>, which writes the zlib header and the Adler-32 itself, and the
/// per-chunk checksum from the table below.
/// </para>
/// </remarks>
public static class PngWriter
{
    // RFC 2083 section 3.1. The eight octets that begin every PNG file.
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Section 15.1: pHYs states the resolution in pixels a metre, and an inch is this many
    // metres exactly, so the conversion is a division and not an approximation.
    private const double MetresPerInch = 0.0254;

    // Section 4.1.1: the only bit depth this writer produces, for either colour type.
    private const byte BitDepth = 8;

    // Section 4.1.1, and the two values of PngColorType.
    private const byte GrayscaleColorType = 0;
    private const byte TruecolourColorType = 2;

    // Section 9.2: filter type 0 means the scanline is stored as it is. A print page is
    // mostly runs of one colour, which deflate already encodes well, so the filters that
    // cost a pass over every pixel buy little here.
    private const byte NoFilter = 0;

    // Section 15.1: unit 1 is the metre. Unit 0 would leave only an aspect ratio.
    private const byte MetreUnit = 1;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// Encodes one image.
    /// </summary>
    /// <param name="pixels">
    /// The bitmap, top line first, with no padding between the lines. Chunky pixels in the
    /// given colour type, so the length is <paramref name="height"/> times
    /// <paramref name="width"/> times one octet for <see cref="PngColorType.Grayscale8"/> or
    /// three for <see cref="PngColorType.Rgb8"/>.
    /// </param>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <param name="height">The height of the bitmap, in pixels.</param>
    /// <param name="colorType">How many octets a pixel occupies, and what they mean.</param>
    /// <param name="dpi">The resolution the page was rendered at, written to <c>pHYs</c>.</param>
    /// <returns>The complete PNG file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension or the resolution is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="pixels"/> is not the size the dimensions give.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> pixels, int width, int height, PngColorType colorType, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);

        var stride = width * BytesPerPixel(colorType);
        if (pixels.Length != stride * height)
        {
            throw new ArgumentException(
                $"A {width} by {height} image of this colour type is {stride * height} octets, and {pixels.Length} were given.",
                nameof(pixels));
        }

        using MemoryStream file = new();
        file.Write(Signature);
        WriteChunk(file, "IHDR"u8, Header(width, height, colorType));
        WriteChunk(file, "pHYs"u8, PhysicalDimensions(dpi));
        WriteChunk(file, "IDAT"u8, Compress(pixels, stride, height));
        WriteChunk(file, "IEND"u8, []);
        return file.ToArray();
    }

    private static int BytesPerPixel(PngColorType colorType) => colorType == PngColorType.Rgb8 ? 3 : 1;

    // Section 11.2.2.
    private static byte[] Header(int width, int height, PngColorType colorType)
    {
        var data = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)height);
        data[8] = BitDepth;
        data[9] = colorType == PngColorType.Rgb8 ? TruecolourColorType : GrayscaleColorType;
        // Compression method 0 (deflate), filter method 0, interlace method 0 (none) are the
        // only values the specification defines, and data is already zero.
        return data;
    }

    // Section 15.1. Both axes carry the same figure: a page is rendered at one resolution.
    private static byte[] PhysicalDimensions(int dpi)
    {
        var perMetre = (uint)Math.Round(dpi / MetresPerInch, MidpointRounding.AwayFromZero);
        var data = new byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(data, perMetre);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), perMetre);
        data[8] = MetreUnit;
        return data;
    }

    // Section 9.2: the compressed stream is every scanline in turn, each preceded by its
    // filter type. The lines are written straight into the deflate stream rather than
    // gathered first, because an A4 page at 300 dots an inch in colour is about 26 MB.
    private static byte[] Compress(ReadOnlySpan<byte> pixels, int stride, int height)
    {
        using MemoryStream compressed = new();
        using (ZLibStream deflate = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                deflate.WriteByte(NoFilter);
                deflate.Write(pixels.Slice(y * stride, stride));
            }
        }

        return compressed.ToArray();
    }

    // Section 5.3: length, type, data, then a checksum over the type and the data.
    private static void WriteChunk(Stream file, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        file.Write(length);
        file.Write(type);
        file.Write(data);

        Span<byte> crc = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
        file.Write(crc);
    }

    // Section 5.5. The standard CRC-32, written here rather than taken from
    // System.IO.Hashing: a package for one checksum is not a trade worth making.
    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var register = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            register = CrcTable[(register ^ value) & 0xFF] ^ (register >> 8);
        }

        foreach (var value in data)
        {
            register = CrcTable[(register ^ value) & 0xFF] ^ (register >> 8);
        }

        return register ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var index = 0u; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
