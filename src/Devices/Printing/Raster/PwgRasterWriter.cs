using System.Buffers.Binary;
using System.Text;

namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Writes a PWG Raster document, the format every IPP Everywhere printer reads.
/// </summary>
/// <remarks>
/// PWG 5102.4 defines one stream that carries every page: a synchronization word, then a
/// header and a compressed bitmap for each page. That is why a converted document stays one
/// document and needs no multi-document IPP job.
/// </remarks>
public sealed class PwgRasterWriter : RasterWriter
{
    // PWG 5102.4 section 4.2: the file begins with "RaS2", and every integer wider than an
    // octet is in network byte order.
    private static ReadOnlySpan<byte> SyncWord => "RaS2"u8;

    // Section 4.3: one fixed-size header for each page.
    private const int HeaderLength = 1796;

    // Section 4.3.1.2: a CString field holds up to 63 characters and a NUL.
    private const int CStringLength = 64;

    // Section 4.3.1.4, Table 3.
    private const uint SgrayColorSpace = 18;
    private const uint SrgbColorSpace = 19;

    // Section 4.3.1.3, Table 2: chunky is the only order PWG Raster uses.
    private const uint ChunkyColorOrder = 0;

    private const int PointsPerInch = 72;

    // One millimetre, in hundredths: a page named with a size further from its own is
    // reported by strict firmware as a page size mismatch.
    private const int NameTolerance = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="PwgRasterWriter"/> class and writes the
    /// synchronization word.
    /// </summary>
    /// <param name="destination">The stream the document is written to. Not disposed by this instance.</param>
    /// <param name="options">What every page of the document has in common.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resolution or the copy count is not positive.</exception>
    public PwgRasterWriter(Stream destination, RasterOptions options)
        : base(destination, options, SyncWord)
    {
    }

    /// <inheritdoc />
    protected override void WritePageHeader(int width, int height, int stride, int crossFeed, int feed)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        header.Clear();

        // Section 4.3, Table 1. Every offset below is the one the table names, and every
        // field the table calls Reserved stays zero.
        WriteCString(header, 0, "PwgRaster");
        WriteUInt32(header, 268, 0);                                        // CutMedia
        WriteUInt32(header, 272, Options.Duplex is DuplexMode.LongEdge or DuplexMode.ShortEdge ? 1u : 0u);
        WriteUInt32(header, 276, (uint)Options.ResolutionDpi);             // HWResolution, cross feed
        WriteUInt32(header, 280, (uint)Options.ResolutionDpi);             // HWResolution, feed
        WriteUInt32(header, 300, 0);                                        // InsertSheet
        WriteUInt32(header, 304, 0);                                        // Jog
        WriteUInt32(header, 308, 0);                                        // LeadingEdge: ShortEdgeFirst
        WriteUInt32(header, 324, 0);                                        // MediaPosition
        WriteUInt32(header, 328, 0);                                        // MediaWeightMetric
        WriteUInt32(header, 340, (uint)Options.Copies);                    // NumCopies
        WriteUInt32(header, 344, 0);                                        // Orientation: portrait
        WriteUInt32(header, 352, (uint)ToPoints(width));                    // PageSize, width
        WriteUInt32(header, 356, (uint)ToPoints(height));                   // PageSize, height
        WriteUInt32(header, 368, Options.Duplex == DuplexMode.ShortEdge ? 1u : 0u);
        WriteUInt32(header, 372, (uint)width);
        WriteUInt32(header, 376, (uint)height);
        WriteUInt32(header, 384, 8);                                        // BitsPerColor
        WriteUInt32(header, 388, (uint)(BytesPerPixel * 8));               // BitsPerPixel
        WriteUInt32(header, 392, (uint)stride);                             // BytesPerLine
        WriteUInt32(header, 396, ChunkyColorOrder);
        WriteUInt32(header, 400, Options.ColorSpace == RasterColorSpace.Srgb8 ? SrgbColorSpace : SgrayColorSpace);
        WriteUInt32(header, 420, (uint)BytesPerPixel);                     // NumColors
        WriteUInt32(header, 452, (uint)Options.TotalPageCount);

        WriteInt32(header, 456, crossFeed);
        WriteInt32(header, 460, feed);

        // Section 4.3.2.9: the whole page, as CUPS writes it. Firmware may read a zero box
        // as an empty page.
        WriteUInt32(header, 464, 0);                                        // ImageBoxLeft
        WriteUInt32(header, 468, 0);                                        // ImageBoxTop
        WriteUInt32(header, 472, (uint)width);                              // ImageBoxRight
        WriteUInt32(header, 476, (uint)height);                             // ImageBoxBottom
        WriteUInt32(header, 484, 0);                                        // PrintQuality: default
        WriteUInt32(header, 508, 0);                                        // VendorIdentifier
        WriteUInt32(header, 512, 0);                                        // VendorLength
        WriteCString(header, 1732, PageSizeName(width, height));

        Destination.Write(header);
    }

    // Rounded down, as CUPS does, so the page never claims to be larger than it is.
    private int ToPoints(int pixels) =>
        (int)Math.Floor(((double)pixels * PointsPerInch / Options.ResolutionDpi) + 1e-6);

    private string? PageSizeName(int width, int height) =>
        Fits(Options.MediaName, width, height)
            ? Options.MediaName
            : Options.MediaSizeNames.FirstOrDefault(name => Fits(name, width, height));

    private bool Fits(string? name, int width, int height) =>
        PwgMediaNames.TryParse(name, out var size)
        && size is not null
        && Math.Abs(size.Width.HundredthsOfMillimeter - ToHundredthsOfMillimeter(width)) <= NameTolerance
        && Math.Abs(size.Height.HundredthsOfMillimeter - ToHundredthsOfMillimeter(height)) <= NameTolerance;

    private double ToHundredthsOfMillimeter(int pixels) => pixels * 2540.0 / Options.ResolutionDpi;

    private static void WriteCString(Span<byte> header, int offset, string? value)
    {
        if (String.IsNullOrEmpty(value))
        {
            return;
        }

        var field = header.Slice(offset, CStringLength);
        var written = Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, CStringLength - 1)), field);
        field[written] = 0;
    }

    private static void WriteUInt32(Span<byte> header, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(offset, sizeof(uint)), value);

    private static void WriteInt32(Span<byte> header, int offset, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(header.Slice(offset, sizeof(int)), value);
}
