using System.Buffers.Binary;

namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Writes an Apple Raster (URF) document, the format AirPrint printers read and a macOS CUPS
/// queue passes to them unfiltered.
/// </summary>
/// <remarks>
/// The layout is the one CUPS writes in <c>cups/raster-stream.c</c>: <c>UNIRAST</c> and a NUL,
/// the page count, then a 32-octet header and a compressed bitmap for each page. The bitmap
/// is encoded as PWG Raster's is. A back side is reordered as <see cref="RasterOptions.SheetBack"/>
/// says, because URF names no transform: the printer states its coordinate system for a back
/// side in the <c>DM</c> keyword of <c>urf-supported</c>.
/// </remarks>
public sealed class UrfWriter : RasterWriter
{
    private const int FileHeaderLength = 12;
    private const int HeaderLength = 32;

    // The colour space octet, as cupsRasterWriteHeader numbers it.
    private const byte SgrayColorSpace = 0;
    private const byte SrgbColorSpace = 1;

    // The duplex octet: 1 one-sided, 2 two-sided on the short edge, 3 on the long edge.
    private const byte OneSided = 1;
    private const byte ShortEdge = 2;
    private const byte LongEdge = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="UrfWriter"/> class and writes the file
    /// header, which carries <see cref="RasterOptions.TotalPageCount"/>.
    /// </summary>
    /// <param name="destination">The stream the document is written to. Not disposed by this instance.</param>
    /// <param name="options">What every page of the document has in common.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resolution or the copy count is not positive.</exception>
    public UrfWriter(Stream destination, RasterOptions options)
        : base(destination, options, FileHeader(options))
    {
    }

    /// <inheritdoc />
    protected override void WritePageHeader(int width, int height, int stride, int crossFeed, int feed)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        header.Clear();

        header[0] = (byte)(BytesPerPixel * 8);
        header[1] = Options.ColorSpace == RasterColorSpace.Srgb8 ? SrgbColorSpace : SgrayColorSpace;
        header[2] = Options.Duplex switch
        {
            DuplexMode.LongEdge => LongEdge,
            DuplexMode.ShortEdge => ShortEdge,
            _ => OneSided,
        };
        BinaryPrimitives.WriteUInt32BigEndian(header[12..], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[16..], (uint)height);
        BinaryPrimitives.WriteUInt32BigEndian(header[20..], (uint)Options.ResolutionDpi);

        Destination.Write(header);
    }

    // The base constructor checks the options for null, after this runs.
    private static byte[] FileHeader(RasterOptions? options)
    {
        var header = new byte[FileHeaderLength];
        "UNIRAST\0"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)(options?.TotalPageCount ?? 0));
        return header;
    }
}
