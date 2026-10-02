using System.Buffers.Binary;

namespace AdaptArch.Devices.Printing.Synthesis;

// What a PDF needs to carry a JPEG unchanged: its size, its colour components, its density,
// the EXIF orientation it is to be shown at, and whether an Adobe marker inverts its CMYK.
// The pixels stay compressed, so baseline and progressive files cost the same and nothing
// here decodes them.
internal sealed record JpegInfo(int Width, int Height, int Components, double? Dpi, bool AdobeInverted, int Orientation = 1)
{
    public static JpegInfo Read(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidDataException("The image is not a JPEG: it lacks the start-of-image marker.");
        }

        double? jfifDpi = null;
        (double? Dpi, int Orientation) exif = (null, 1);
        var adobe = false;
        var at = 2;
        while (TryReadSegment(jpeg, ref at, out var marker, out var segment))
        {
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && segment.Length >= 6)
            {
                // JFIF states the density of the file itself; a camera writes EXIF and no JFIF.
                return Frame(marker, segment, jfifDpi ?? exif.Dpi, adobe, exif.Orientation);
            }

            if (marker == 0xE0 && segment.Length >= 12 && segment[..5].SequenceEqual("JFIF\0"u8))
            {
                jfifDpi = Density(segment[7], BinaryPrimitives.ReadUInt16BigEndian(segment[8..]));
            }
            else if (marker == 0xE1 && segment.Length >= 14 && segment[..6].SequenceEqual("Exif\0\0"u8))
            {
                exif = Exif(segment[6..]);
            }

            adobe |= marker == 0xEE && segment.Length >= 5 && segment[..5].SequenceEqual("Adobe"u8);
        }

        throw new InvalidDataException("The JPEG has no frame header the library can read.");
    }

    // The resolution and the orientation of the first image file directory of a TIFF header,
    // which is what EXIF is. Nothing here is trusted: a field past the end is simply absent.
    private static (double? Dpi, int Orientation) Exif(ReadOnlySpan<byte> tiff)
    {
        var little = tiff[..2].SequenceEqual("II"u8);
        if (!little && !tiff[..2].SequenceEqual("MM"u8))
        {
            return (null, 1);
        }

        var directory = (int)Math.Min(U32(tiff, 4, little), Int32.MaxValue);
        if (directory < 0 || directory + 2 > tiff.Length)
        {
            return (null, 1);
        }

        ExifFields fields = new();
        int count = U16(tiff, directory, little);
        for (var index = 0; index < count && directory + 2 + ((index + 1) * 12) <= tiff.Length; index++)
        {
            ReadEntry(tiff, directory + 2 + (index * 12), little, ref fields);
        }

        return (fields.Dpi, fields.Orientation is >= 1 and <= 8 ? fields.Orientation : 1);
    }

    // One 12-octet directory entry: the tag, its type, a count, then the value or an offset
    // to it. Only the three tags the layout needs are read, each at the type EXIF gives it.
    private static void ReadEntry(ReadOnlySpan<byte> tiff, int entry, bool little, ref ExifFields fields)
    {
        var tag = U16(tiff, entry, little);
        var type = U16(tiff, entry + 2, little);
        if (tag == 0x0112 && type == 3)
        {
            fields.Orientation = U16(tiff, entry + 8, little);
        }
        else if (tag == 0x0128 && type == 3)
        {
            fields.Unit = U16(tiff, entry + 8, little);
        }
        else if (tag == 0x011A && type == 5)
        {
            var offset = (int)Math.Min(U32(tiff, entry + 8, little), Int32.MaxValue);
            if (offset >= 0 && offset + 8 <= tiff.Length && U32(tiff, offset + 4, little) is > 0 and var denominator)
            {
                fields.Resolution = (double)U32(tiff, offset, little) / denominator;
            }
        }
    }

    // EXIF units: 2 is dots per inch and 3 dots per centimetre; anything else states no density.
    private struct ExifFields
    {
        public double? Resolution;
        public int Unit = 2;
        public int Orientation = 1;

        public ExifFields()
        {
        }

        public readonly double? Dpi => Resolution is > 0 ? Unit switch { 2 => Resolution, 3 => Resolution * 2.54, _ => null } : null;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

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

    // PDF's DCTDecode is the baseline, extended and progressive Huffman process at eight
    // bits (SOF0, SOF1, SOF2). A lossless, arithmetic or hierarchical frame, or twelve-bit
    // samples, would be written as DCTDecode and fail at the printer with no hint.
    private static JpegInfo Frame(byte marker, ReadOnlySpan<byte> segment, double? dpi, bool adobe, int orientation)
    {
        if (marker is not (0xC0 or 0xC1 or 0xC2))
        {
            throw new NotSupportedException(
                $"The JPEG uses frame marker SOF{marker - 0xC0} (0x{marker:X2}), a lossless, arithmetic or hierarchical process that PDF readers and printers do not decode. Re-encode it as a baseline or progressive JPEG.");
        }

        if (segment[0] != 8)
        {
            throw new NotSupportedException(
                $"The JPEG has {segment[0]}-bit samples, and PDF readers and printers decode 8-bit JPEG only. Re-encode it at 8 bits.");
        }

        var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
        var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        var components = segment[5];
        if (width == 0 || height == 0 || components is not (1 or 3 or 4))
        {
            throw new InvalidDataException("The JPEG has no frame header the library can read.");
        }

        return new JpegInfo(width, height, components, dpi, adobe && components == 4, orientation);
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
