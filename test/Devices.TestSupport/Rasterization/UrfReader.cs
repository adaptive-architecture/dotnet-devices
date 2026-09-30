#nullable enable
using System.Buffers.Binary;
using System.Collections.Generic;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// Reads an Apple Raster (URF) document back into pages of pixels.
/// </summary>
/// <remarks>
/// Written against what CUPS reads in <c>cups/raster-stream.c</c> and not against
/// <c>UrfWriter</c>, for the reason <see cref="PwgRasterReader"/> gives. The bitmap is
/// encoded as PWG Raster's is, so its decoder is shared.
/// </remarks>
public static class UrfReader
{
    private const int FileHeaderLength = 12;
    private const int HeaderLength = 32;

    /// <summary>One page of a document, as the printer would read it.</summary>
    public sealed record UrfPage(
        byte[] Pixels,
        int Width,
        int Height,
        int BitsPerPixel,
        int ColorSpace,
        int Duplex,
        int ResolutionDpi);

    /// <summary>
    /// Reads the page count of the file header and every page of one document.
    /// </summary>
    /// <param name="document">The whole URF stream, file header included.</param>
    /// <returns>The page count the file header declares, and the pages in the order the document carries them.</returns>
    public static (int PageCount, IReadOnlyList<UrfPage> Pages) Read(byte[] document)
    {
        if (document.Length < FileHeaderLength || System.Text.Encoding.ASCII.GetString(document, 0, 8) != "UNIRAST\0")
        {
            throw new InvalidDataException("The document does not begin with the URF file header.");
        }

        var pageCount = (int)BinaryPrimitives.ReadUInt32BigEndian(document.AsSpan(8, sizeof(uint)));
        List<UrfPage> pages = [];
        var offset = FileHeaderLength;
        while (offset < document.Length)
        {
            var header = document.AsSpan(offset, HeaderLength);
            offset += HeaderLength;

            var bitsPerPixel = header[0];
            var width = (int)BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
            var height = (int)BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
            var colorSpace = header[1];
            var duplex = header[2];
            var resolution = (int)BinaryPrimitives.ReadUInt32BigEndian(header[20..]);

            var pixels = PwgRasterReader.ReadBitmap(document, ref offset, width * (bitsPerPixel / 8), height, bitsPerPixel / 8);
            pages.Add(new UrfPage(pixels, width, height, bitsPerPixel, colorSpace, duplex, resolution));
        }

        return (pageCount, pages);
    }
}
