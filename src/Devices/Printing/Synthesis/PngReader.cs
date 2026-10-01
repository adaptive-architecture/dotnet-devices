using System.Buffers.Binary;
using System.IO.Compression;

namespace AdaptArch.Devices.Printing.Synthesis;

// Decodes a PNG to 8-bit samples: grey or RGB, with the alpha apart, which is how a PDF image
// and its soft mask hold them. Every colour type, bit depth and Adam7 interlacing is read.
internal static class PngReader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly (int X, int Y, int DX, int DY)[] Passes =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    public static DecodedImage Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length < 8 || !png[..8].SequenceEqual(Signature))
        {
            throw new InvalidDataException("The image is not a PNG: it lacks the PNG signature.");
        }

        var width = 0;
        var height = 0;
        var depth = 0;
        var colorType = 0;
        var interlaced = false;
        byte[] palette = [];
        byte[] transparency = [];
        double? dpi = null;
        using MemoryStream compressed = new();

        var at = 8;
        while (at + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            var type = png.Slice(at + 4, 4);
            var data = png.Slice(at + 8, length);
            at += 12 + length;

            if (type.SequenceEqual("IHDR"u8))
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                depth = data[8];
                colorType = data[9];
                interlaced = data[12] == 1;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                transparency = data.ToArray();
            }
            else if (type.SequenceEqual("pHYs"u8) && data[8] == 1)
            {
                // Pixels per metre.
                dpi = BinaryPrimitives.ReadUInt32BigEndian(data) * 0.0254;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || depth is not (1 or 2 or 4 or 8 or 16) || colorType is not (0 or 2 or 3 or 4 or 6))
        {
            throw new InvalidDataException("The PNG has no valid image header.");
        }

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new();
        zlib.CopyTo(raw);

        PngFormat format = new(width, height, depth, colorType, palette, transparency);
        return interlaced ? Deinterlace(raw.ToArray(), format, dpi) : Progressive(raw.ToArray(), format, dpi);
    }

    private static DecodedImage Progressive(byte[] raw, PngFormat format, double? dpi)
    {
        var image = format.Empty(dpi);
        var offset = 0;
        Unfilter(raw, ref offset, format, format.Width, format.Height, (x, y) => (x, y), image);
        return image;
    }

    private static DecodedImage Deinterlace(byte[] raw, PngFormat format, double? dpi)
    {
        var image = format.Empty(dpi);
        var offset = 0;
        foreach ((var startX, var startY, var stepX, var stepY) in Passes)
        {
            var passWidth = (format.Width - startX + stepX - 1) / stepX;
            var passHeight = (format.Height - startY + stepY - 1) / stepY;
            if (passWidth > 0 && passHeight > 0)
            {
                Unfilter(raw, ref offset, format, passWidth, passHeight, (x, y) => (startX + (x * stepX), startY + (y * stepY)), image);
            }
        }

        return image;
    }

    private static void Unfilter(byte[] raw, ref int offset, PngFormat format, int width, int height, Func<int, int, (int X, int Y)> at, DecodedImage image)
    {
        var bitsPerPixel = format.Channels * format.Depth;
        var stride = ((width * bitsPerPixel) + 7) / 8;
        var bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        var previous = new byte[stride];
        var current = new byte[stride];

        for (var y = 0; y < height; y++)
        {
            if (offset + 1 + stride > raw.Length)
            {
                throw new InvalidDataException("The PNG image data ends before its last line.");
            }

            var filter = raw[offset];
            raw.AsSpan(offset + 1, stride).CopyTo(current);
            offset += 1 + stride;
            for (var index = 0; index < stride; index++)
            {
                var left = index >= bytesPerPixel ? current[index - bytesPerPixel] : 0;
                int up = previous[index];
                var upLeft = index >= bytesPerPixel ? previous[index - bytesPerPixel] : 0;
                current[index] += Predict(filter, left, up, upLeft);
            }

            for (var x = 0; x < width; x++)
            {
                (var targetX, var targetY) = at(x, y);
                format.Store(image, (targetY * format.Width) + targetX, current, x);
            }

            (previous, current) = (current, previous);
        }
    }

    private static byte Predict(byte filter, int left, int up, int upLeft)
    {
        if (filter == 1)
        {
            return (byte)left;
        }

        if (filter == 2)
        {
            return (byte)up;
        }

        if (filter == 3)
        {
            return (byte)((left + up) / 2);
        }

        if (filter == 4)
        {
            var estimate = left + up - upLeft;
            var toLeft = Math.Abs(estimate - left);
            var toUp = Math.Abs(estimate - up);
            var toUpLeft = Math.Abs(estimate - upLeft);
            if (toLeft <= toUp && toLeft <= toUpLeft)
            {
                return (byte)left;
            }

            return (byte)(toUp <= toUpLeft ? up : upLeft);
        }

        return 0;
    }

    private sealed record PngFormat(int Width, int Height, int Depth, int ColorType, byte[] Palette, byte[] Transparency)
    {
        public int Channels => ColorType == 2 ? 3 : ColorType == 4 ? 2 : ColorType == 6 ? 4 : 1;

        private bool IsColor => ColorType is 2 or 3 or 6;

        private bool HasAlpha => ColorType is 4 or 6 || Transparency.Length > 0;

        public DecodedImage Empty(double? dpi) => new(Width, Height, IsColor ? 3 : 1, HasAlpha, dpi);

        public void Store(DecodedImage image, int pixel, byte[] line, int x)
        {
            var first = x * Channels;
            if (ColorType == 3)
            {
                var index = Sample(line, first);
                if ((index * 3) + 2 >= Palette.Length)
                {
                    throw new InvalidDataException("The PNG names a palette entry it does not have.");
                }

                Palette.AsSpan(index * 3, 3).CopyTo(image.Color.AsSpan(pixel * 3));
                SetAlpha(image, pixel, index < Transparency.Length ? Transparency[index] : (byte)255);
                return;
            }

            var colors = IsColor ? 3 : 1;
            var keyed = Transparency.Length >= colors * 2;
            for (var channel = 0; channel < colors; channel++)
            {
                var value = Sample(line, first + channel);
                image.Color[(pixel * colors) + channel] = value;
                keyed &= Transparency.Length >= colors * 2 && value == Key(channel);
            }

            SetAlpha(image, pixel, Channels > colors ? Sample(line, first + colors) : keyed ? (byte)0 : (byte)255);
        }

        private static void SetAlpha(DecodedImage image, int pixel, byte alpha)
        {
            if (image.Alpha is not null)
            {
                image.Alpha[pixel] = alpha;
            }
        }

        // The sample at the index: the palette index for type 3, scaled to eight bits
        // otherwise. Sixteen bits keep their high octet.
        private byte Sample(byte[] line, int index)
        {
            if (Depth == 16)
            {
                return line[index * 2];
            }

            if (Depth == 8)
            {
                return line[index];
            }

            var bits = index * Depth;
            var value = (line[bits / 8] >> (8 - Depth - (bits % 8))) & ((1 << Depth) - 1);
            return ColorType == 3 ? (byte)value : (byte)(value * 255 / ((1 << Depth) - 1));
        }

        // The key a tRNS chunk names for a grey or RGB image, at eight bits.
        private int Key(int channel)
        {
            var value = BinaryPrimitives.ReadUInt16BigEndian(Transparency.AsSpan(channel * 2));
            return Depth == 16 ? value >> 8 : value * 255 / ((1 << Depth) - 1);
        }
    }
}
