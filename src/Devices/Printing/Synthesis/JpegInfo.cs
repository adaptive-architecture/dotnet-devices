using System.Buffers.Binary;

namespace AdaptArch.Devices.Printing.Synthesis;

// What a PDF needs to carry a JPEG unchanged: its size, its colour components, its density,
// and whether an Adobe marker inverts its CMYK. The pixels stay compressed, so baseline and
// progressive files cost the same and nothing here decodes them.
internal sealed record JpegInfo(int Width, int Height, int Components, double? Dpi, bool AdobeInverted)
{
    public static JpegInfo Read(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidDataException("The image is not a JPEG: it lacks the start-of-image marker.");
        }

        double? dpi = null;
        var adobe = false;
        var at = 2;
        while (TryReadSegment(jpeg, ref at, out var marker, out var segment))
        {
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && segment.Length >= 6)
            {
                return Frame(segment, dpi, adobe);
            }

            if (marker == 0xE0 && segment.Length >= 12 && segment[..5].SequenceEqual("JFIF\0"u8))
            {
                dpi = Density(segment[7], BinaryPrimitives.ReadUInt16BigEndian(segment[8..]));
            }

            adobe |= marker == 0xEE && segment.Length >= 5 && segment[..5].SequenceEqual("Adobe"u8);
        }

        throw new InvalidDataException("The JPEG has no frame header the library can read.");
    }

    // The next marker that carries a length, past fill octets and the markers that stand alone.
    private static bool TryReadSegment(ReadOnlySpan<byte> jpeg, ref int at, out byte marker, out ReadOnlySpan<byte> segment)
    {
        while (at + 4 <= jpeg.Length)
        {
            marker = jpeg[at + 1];
            if (jpeg[at] != 0xFF || marker == 0xFF)
            {
                at++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                at += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(at + 2)..]);
            segment = jpeg.Slice(at + 4, Math.Max(0, Math.Min(length - 2, jpeg.Length - at - 4)));
            at += 2 + length;
            return true;
        }

        marker = 0;
        segment = default;
        return false;
    }

    private static JpegInfo Frame(ReadOnlySpan<byte> segment, double? dpi, bool adobe)
    {
        var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var components = segment[5];
        if (width == 0 || height == 0 || components is not (1 or 3 or 4))
        {
            throw new InvalidDataException("The JPEG has no frame header the library can read.");
        }

        return new JpegInfo(width, height, components, dpi, adobe && components == 4);
    }

    // JFIF units: 1 is dots per inch, 2 dots per centimetre, and 0 only an aspect ratio.
    private static double? Density(byte units, ushort value)
    {
        if (value == 0)
        {
            return null;
        }

        if (units == 1)
        {
            return value;
        }

        return units == 2 ? value * 2.54 : null;
    }
}
