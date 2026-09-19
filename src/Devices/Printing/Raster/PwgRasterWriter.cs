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
/// <para>
/// Pages are written one at a time and nothing is buffered, because an A4 page at 300 dots
/// an inch in <see cref="PwgRasterColorSpace.Srgb8"/> is about 26 MB before compression.
/// A duplex back side the printer reads transformed is the one exception: its reordered
/// copy lives only until the page is encoded.
/// </para>
/// <para>
/// The synchronization word is written by the constructor, so a document with no page is
/// still a valid file.
/// </para>
/// </remarks>
public sealed class PwgRasterWriter
{
    // PWG 5102.4 section 4.2: the file begins with "RaS2", and every integer wider than an
    // octet is in network byte order.
    private static readonly byte[] SyncWord = "RaS2"u8.ToArray();

    // Section 4.3: one fixed-size header for each page.
    private const int HeaderLength = 1796;

    // Section 4.3.1.2: a CString field holds up to 63 characters and a NUL.
    private const int CStringLength = 64;

    // The longest run either encoding can name, and the longest run of equal lines.
    private const int MaxRun = 128;
    private const int MaxLineRepeat = 256;

    // Section 4.3.1.4, Table 3.
    private const uint SgrayColorSpace = 18;
    private const uint SrgbColorSpace = 19;

    // Section 4.3.1.3, Table 2: chunky is the only order PWG Raster uses.
    private const uint ChunkyColorOrder = 0;

    private const int PointsPerInch = 72;

    private readonly Stream _destination;
    private readonly PwgRasterOptions _options;
    private readonly int _bytesPerPixel;
    private int _pagesWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="PwgRasterWriter"/> class and writes the
    /// synchronization word.
    /// </summary>
    /// <param name="destination">The stream the document is written to. Not disposed by this instance.</param>
    /// <param name="options">What every page of the document has in common.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resolution or the copy count is not positive.</exception>
    public PwgRasterWriter(Stream destination, PwgRasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ResolutionDpi, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Copies, 1);

        _destination = destination;
        _options = options;
        _bytesPerPixel = options.ColorSpace == PwgRasterColorSpace.Srgb8 ? 3 : 1;
        destination.Write(SyncWord);
    }

    /// <summary>
    /// Gets the number of octets one line of a bitmap of the given width occupies.
    /// </summary>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <returns>The length of one line, in octets.</returns>
    public int BytesPerLine(int width) => width * _bytesPerPixel;

    /// <summary>
    /// Writes one page: its header, then its bitmap.
    /// </summary>
    /// <param name="pixels">
    /// The bitmap, top line first, with no padding between the lines. Chunky pixels in the
    /// colour space of the options, so the length is <paramref name="height"/> times
    /// <see cref="BytesPerLine"/> of <paramref name="width"/>.
    /// </param>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <param name="height">The height of the bitmap, in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a dimension is not positive.</exception>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="pixels"/> is not the size the dimensions give.</exception>
    public void WritePage(ReadOnlySpan<byte> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        var stride = BytesPerLine(width);
        if (pixels.Length != stride * height)
        {
            throw new ArgumentException(
                $"A {width} by {height} page of this colour space is {stride * height} octets, and {pixels.Length} were given.",
                nameof(pixels));
        }

        (var crossFeed, var feed) = BackSideTransforms();
        WriteHeader(width, height, stride, crossFeed, feed);
        if (crossFeed == 1 && feed == 1)
        {
            WriteBitmap(pixels, stride);
        }
        else
        {
            WriteBitmap(OrientForBackSide(pixels, width, height, stride, crossFeed, feed), stride);
        }

        _pagesWritten++;
    }

    // PWG 5102.4 Table 9. A front side always uses 1 and 1; only the back of a duplex sheet
    // is written in another coordinate system, and which one depends on the edge the sheet
    // turns on as well as on what the printer said.
    private (int CrossFeed, int Feed) BackSideTransforms()
    {
        // Pages are counted from the first, so an odd index is a back side.
        var isBackSide = _pagesWritten % 2 == 1;
        if (!isBackSide || _options.Duplex is not (DuplexMode.LongEdge or DuplexMode.ShortEdge))
        {
            return (1, 1);
        }

        if (_options.Duplex == DuplexMode.LongEdge)
        {
            return _options.SheetBack switch
            {
                PwgRasterSheetBack.Flipped => (1, -1),
                PwgRasterSheetBack.Rotated => (-1, -1),
                _ => (1, 1),
            };
        }

        return _options.SheetBack switch
        {
            PwgRasterSheetBack.Flipped => (-1, 1),
            PwgRasterSheetBack.ManualTumble => (-1, -1),
            _ => (1, 1),
        };
    }

    // PWG 5102.4 section 4.3.2.8: the header fields declare the orientation of the
    // bitmap, and the bitmap itself must arrive that way — the printer performs no
    // transformation of its own. A back side the table moves is therefore reordered
    // here, pixel by pixel rather than octet by octet, because a colour pixel is
    // three octets that must stay together. The copy is transient: it is encoded
    // straight into the destination and released.
    private byte[] OrientForBackSide(ReadOnlySpan<byte> pixels, int width, int height, int stride, int crossFeed, int feed)
    {
        var oriented = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            var sourceY = feed == 1 ? y : height - 1 - y;
            var sourceLine = pixels.Slice(sourceY * stride, stride);
            var targetLine = oriented.AsSpan(y * stride, stride);
            if (crossFeed == 1)
            {
                sourceLine.CopyTo(targetLine);
            }
            else
            {
                for (var x = 0; x < width; x++)
                {
                    var source = sourceLine.Slice((width - 1 - x) * _bytesPerPixel, _bytesPerPixel);
                    source.CopyTo(targetLine.Slice(x * _bytesPerPixel, _bytesPerPixel));
                }
            }
        }

        return oriented;
    }

    private void WriteHeader(int width, int height, int stride, int crossFeed, int feed)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        header.Clear();

        // Section 4.3, Table 1. Every offset below is the one the table names, and every
        // field the table calls Reserved stays zero.
        WriteCString(header, 0, "PwgRaster");
        WriteUInt32(header, 268, 0);                                        // CutMedia
        WriteUInt32(header, 272, _options.Duplex is DuplexMode.LongEdge or DuplexMode.ShortEdge ? 1u : 0u);
        WriteUInt32(header, 276, (uint)_options.ResolutionDpi);             // HWResolution, cross feed
        WriteUInt32(header, 280, (uint)_options.ResolutionDpi);             // HWResolution, feed
        WriteUInt32(header, 300, 0);                                        // InsertSheet
        WriteUInt32(header, 304, 0);                                        // Jog
        WriteUInt32(header, 308, 0);                                        // LeadingEdge: ShortEdgeFirst
        WriteUInt32(header, 324, 0);                                        // MediaPosition
        WriteUInt32(header, 328, 0);                                        // MediaWeightMetric
        WriteUInt32(header, 340, (uint)_options.Copies);                    // NumCopies
        WriteUInt32(header, 344, 0);                                        // Orientation: portrait
        WriteUInt32(header, 352, (uint)ToPoints(width));                    // PageSize, width
        WriteUInt32(header, 356, (uint)ToPoints(height));                   // PageSize, height
        WriteUInt32(header, 368, _options.Duplex == DuplexMode.ShortEdge ? 1u : 0u);
        WriteUInt32(header, 372, (uint)width);
        WriteUInt32(header, 376, (uint)height);
        WriteUInt32(header, 384, 8);                                        // BitsPerColor
        WriteUInt32(header, 388, (uint)(_bytesPerPixel * 8));               // BitsPerPixel
        WriteUInt32(header, 392, (uint)stride);                             // BytesPerLine
        WriteUInt32(header, 396, ChunkyColorOrder);
        WriteUInt32(header, 400, _options.ColorSpace == PwgRasterColorSpace.Srgb8 ? SrgbColorSpace : SgrayColorSpace);
        WriteUInt32(header, 420, (uint)_bytesPerPixel);                     // NumColors
        WriteUInt32(header, 452, (uint)_options.TotalPageCount);

        WriteInt32(header, 456, crossFeed);
        WriteInt32(header, 460, feed);

        // Section 4.3.2.9: the ImageBox fields are zero when the content area is unknown,
        // which it is: the caller handed us pixels and said nothing about their margins.
        WriteUInt32(header, 484, 0);                                        // PrintQuality: default
        WriteUInt32(header, 508, 0);                                        // VendorIdentifier
        WriteUInt32(header, 512, 0);                                        // VendorLength
        WriteCString(header, 1732, _options.MediaName);

        _destination.Write(header);
    }

    // Section 4.4. Each line is preceded by the number of times it repeats, and its colour
    // values are then run-length encoded.
    private void WriteBitmap(ReadOnlySpan<byte> pixels, int stride)
    {
        var height = pixels.Length / stride;
        var y = 0;
        while (y < height)
        {
            var line = pixels.Slice(y * stride, stride);

            var repeat = 1;
            while (y + repeat < height
                && repeat < MaxLineRepeat
                && pixels.Slice((y + repeat) * stride, stride).SequenceEqual(line))
            {
                repeat++;
            }

            _destination.WriteByte((byte)(repeat - 1));
            WriteLine(line);
            y += repeat;
        }
    }

    private void WriteLine(ReadOnlySpan<byte> line)
    {
        var units = line.Length / _bytesPerPixel;
        var index = 0;
        while (index < units)
        {
            var run = RepeatLength(line, index, units);
            if (run > 1)
            {
                // 1 to 128 repeated colours: "count - 1", then the colour once.
                _destination.WriteByte((byte)(run - 1));
                _destination.Write(Unit(line, index));
                index += run;
                continue;
            }

            var literal = LiteralLength(line, index, units);
            if (literal == 1)
            {
                // A lone colour cannot be a literal run, because those start at two, and
                // "257 - 1" does not fit an octet. It is a repeat of one instead.
                _destination.WriteByte(0);
                _destination.Write(Unit(line, index));
                index++;
                continue;
            }

            // 2 to 128 non-repeating colours: "257 - count", then every colour.
            _destination.WriteByte((byte)(257 - literal));
            _destination.Write(line.Slice(index * _bytesPerPixel, literal * _bytesPerPixel));
            index += literal;
        }
    }

    private int RepeatLength(ReadOnlySpan<byte> line, int index, int units)
    {
        var first = Unit(line, index);
        var run = 1;
        while (index + run < units && run < MaxRun && Unit(line, index + run).SequenceEqual(first))
        {
            run++;
        }

        return run;
    }

    // Colours up to the next pair of equal ones, which starts a cheaper repeat run.
    private int LiteralLength(ReadOnlySpan<byte> line, int index, int units)
    {
        var literal = 1;
        while (index + literal < units
            && literal < MaxRun
            && !(index + literal + 1 < units && Unit(line, index + literal).SequenceEqual(Unit(line, index + literal + 1))))
        {
            literal++;
        }

        return literal;
    }

    private ReadOnlySpan<byte> Unit(ReadOnlySpan<byte> line, int index) =>
        line.Slice(index * _bytesPerPixel, _bytesPerPixel);

    private int ToPoints(int pixels) =>
        (int)Math.Round((double)pixels * PointsPerInch / _options.ResolutionDpi, MidpointRounding.AwayFromZero);

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
