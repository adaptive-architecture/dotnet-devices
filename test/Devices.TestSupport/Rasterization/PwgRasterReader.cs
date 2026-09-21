#nullable enable
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// Reads a PWG Raster document back into pages of pixels.
/// </summary>
/// <remarks>
/// Written against PWG 5102.4 and not against <c>PwgRasterWriter</c>, which is the rule the
/// raster tests of this repository already follow: a reader that shares the writer's idea of
/// the format agrees with it whatever the two of them do.
/// <para>
/// This is what lets a test look at what a rasterizer produced. The pages come back as the
/// chunky, unpadded bitmaps <c>PngWriter.Encode</c> takes, so the round trip needs nothing
/// else.
/// </para>
/// </remarks>
public static class PwgRasterReader
{
    // Section 4.2: the file begins with "RaS2", then one header and one bitmap a page.
    private const int SyncLength = 4;

    // Section 4.3: one fixed-size header for each page.
    private const int HeaderLength = 1796;

    // Section 4.3, Table 1. The offsets inside a page header this reader needs.
    private const int ResolutionOffset = 276;
    private const int WidthOffset = 372;
    private const int HeightOffset = 376;
    private const int BitsPerPixelOffset = 388;
    private const int TotalPageCountOffset = 452;
    private const int CrossFeedTransformOffset = 456;
    private const int FeedTransformOffset = 460;

    /// <summary>One page of a document, as the printer would read it.</summary>
    public sealed record RasterPage(
        byte[] Pixels,
        int Width,
        int Height,
        int BitsPerPixel,
        int ResolutionDpi,
        int TotalPageCount,
        int CrossFeedTransform,
        int FeedTransform)
    {
        public int BytesPerPixel => BitsPerPixel / 8;
    }

    /// <summary>
    /// Reads every page of one document.
    /// </summary>
    /// <param name="document">The whole PWG Raster stream, synchronization word included.</param>
    /// <returns>The pages, in the order the document carries them.</returns>
    public static IReadOnlyList<RasterPage> Read(byte[] document)
    {
        if (document.Length < SyncLength || System.Text.Encoding.ASCII.GetString(document, 0, SyncLength) != "RaS2")
        {
            throw new InvalidDataException("The document does not begin with the PWG Raster synchronization word.");
        }

        List<RasterPage> pages = [];
        var offset = SyncLength;
        while (offset < document.Length)
        {
            var header = document.AsSpan(offset, HeaderLength);
            offset += HeaderLength;

            var width = (int)ReadUInt32(header, WidthOffset);
            var height = (int)ReadUInt32(header, HeightOffset);
            var bitsPerPixel = (int)ReadUInt32(header, BitsPerPixelOffset);
            var stride = width * (bitsPerPixel / 8);

            var pixels = ReadBitmap(document, ref offset, stride, height, bitsPerPixel / 8);
            pages.Add(new RasterPage(
                pixels,
                width,
                height,
                bitsPerPixel,
                (int)ReadUInt32(header, ResolutionOffset),
                (int)ReadUInt32(header, TotalPageCountOffset),
                ReadInt32(header, CrossFeedTransformOffset),
                ReadInt32(header, FeedTransformOffset)));
        }

        return pages;
    }

    // Section 4.4: each line is preceded by the number of times it repeats, and its colour
    // values are then run-length encoded. The page ends when it has as many lines as its
    // header declared, which is what lets the next header be found.
    private static byte[] ReadBitmap(byte[] document, ref int offset, int stride, int height, int bytesPerPixel)
    {
        var pixels = new byte[stride * height];
        var line = new byte[stride];
        var written = 0;

        while (written < pixels.Length)
        {
            var repeat = document[offset++] + 1;

            var filled = 0;
            while (filled < stride)
            {
                var control = document[offset++];
                if (control <= 127)
                {
                    // 1 to 128 repeated colours: the count, then the colour once.
                    var count = control + 1;
                    for (var i = 0; i < count; i++)
                    {
                        Array.Copy(document, offset, line, filled, bytesPerPixel);
                        filled += bytesPerPixel;
                    }

                    offset += bytesPerPixel;
                }
                else
                {
                    // 2 to 128 non-repeating colours: "257 - count", then every colour.
                    var count = 257 - control;
                    Array.Copy(document, offset, line, filled, count * bytesPerPixel);
                    filled += count * bytesPerPixel;
                    offset += count * bytesPerPixel;
                }
            }

            for (var i = 0; i < repeat && written < pixels.Length; i++)
            {
                Array.Copy(line, 0, pixels, written, stride);
                written += stride;
            }
        }

        return pixels;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> header, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(header.Slice(offset, sizeof(uint)));

    private static int ReadInt32(ReadOnlySpan<byte> header, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(header.Slice(offset, sizeof(int)));
}
