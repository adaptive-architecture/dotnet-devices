namespace AdaptArch.Devices.Printing.Raster;

/// <summary>
/// Writes a raster document in which every page is a header and a compressed bitmap:
/// PWG Raster, which <see cref="PwgRasterWriter"/> writes, or Apple Raster, which
/// <see cref="UrfWriter"/> writes.
/// </summary>
/// <remarks>
/// The two formats differ only in their headers. Each line is preceded by the number of
/// times it repeats and its colour values are run-length encoded, as PWG 5102.4 section 4.4
/// describes and CUPS writes for both.
/// <para>
/// Pages are written one at a time and nothing is buffered, because an A4 page at 300 dots
/// an inch in <see cref="RasterColorSpace.Srgb8"/> is about 26 MB before compression.
/// A duplex back side the printer reads transformed is the one exception: its reordered
/// copy lives only until the page is encoded.
/// </para>
/// <para>
/// The file header is written by the constructor, so a document with no page is still a
/// valid file.
/// </para>
/// </remarks>
public abstract class RasterWriter
{
    // The longest run either encoding can name, and the longest run of equal lines.
    private const int MaxRun = 128;
    private const int MaxLineRepeat = 256;

    private int _pagesWritten;

    /// <summary>
    /// Initializes a new instance of the <see cref="RasterWriter"/> class and writes the
    /// file header.
    /// </summary>
    /// <param name="destination">The stream the document is written to. Not disposed by this instance.</param>
    /// <param name="options">What every page of the document has in common.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <c>null</c>.</exception>
    /// <param name="fileHeader">What the file starts with, before the first page.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the resolution or the copy count is not positive.</exception>
    protected RasterWriter(Stream destination, RasterOptions options, ReadOnlySpan<byte> fileHeader)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ResolutionDpi, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Copies, 1);

        Destination = destination;
        Options = options;
        BytesPerPixel = options.ColorSpace == RasterColorSpace.Srgb8 ? 3 : 1;
        destination.Write(fileHeader);
    }

    /// <summary>
    /// Gets what every page of the document has in common.
    /// </summary>
    protected RasterOptions Options { get; }

    /// <summary>
    /// Gets the stream the document is written to.
    /// </summary>
    protected Stream Destination { get; }

    /// <summary>
    /// Gets the number of octets one pixel occupies.
    /// </summary>
    protected int BytesPerPixel { get; }

    /// <summary>
    /// Gets the number of octets one line of a bitmap of the given width occupies.
    /// </summary>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <returns>The length of one line, in octets.</returns>
    public int BytesPerLine(int width) => width * BytesPerPixel;

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
        WritePageHeader(width, height, stride, crossFeed, feed);
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

    /// <summary>
    /// Writes the header of one page.
    /// </summary>
    /// <param name="width">The width of the bitmap, in pixels.</param>
    /// <param name="height">The height of the bitmap, in pixels.</param>
    /// <param name="stride">The length of one line, in octets.</param>
    /// <param name="crossFeed">1, or -1 when the bitmap of this back side is mirrored across the cross-feed direction.</param>
    /// <param name="feed">1, or -1 when the bitmap of this back side is mirrored across the feed direction.</param>
    protected abstract void WritePageHeader(int width, int height, int stride, int crossFeed, int feed);

    // PWG 5102.4 Table 9. A front side always uses 1 and 1; only the back of a duplex sheet
    // is written in another coordinate system, and which one depends on the edge the sheet
    // turns on as well as on what the printer said.
    private (int CrossFeed, int Feed) BackSideTransforms()
    {
        // Pages are counted from the first, so an odd index is a back side.
        var isBackSide = _pagesWritten % 2 == 1;
        if (!isBackSide || Options.Duplex is not (DuplexMode.LongEdge or DuplexMode.ShortEdge))
        {
            return (1, 1);
        }

        if (Options.Duplex == DuplexMode.LongEdge)
        {
            return Options.SheetBack switch
            {
                RasterSheetBack.Flipped => (1, -1),
                RasterSheetBack.Rotated => (-1, -1),
                _ => (1, 1),
            };
        }

        return Options.SheetBack switch
        {
            RasterSheetBack.Flipped => (-1, 1),
            RasterSheetBack.ManualTumble => (-1, -1),
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
                    var source = sourceLine.Slice((width - 1 - x) * BytesPerPixel, BytesPerPixel);
                    source.CopyTo(targetLine.Slice(x * BytesPerPixel, BytesPerPixel));
                }
            }
        }

        return oriented;
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

            Destination.WriteByte((byte)(repeat - 1));
            WriteLine(line);
            y += repeat;
        }
    }

    private void WriteLine(ReadOnlySpan<byte> line)
    {
        var units = line.Length / BytesPerPixel;
        var index = 0;
        while (index < units)
        {
            var run = RepeatLength(line, index, units);
            if (run > 1)
            {
                // 1 to 128 repeated colours: "count - 1", then the colour once.
                Destination.WriteByte((byte)(run - 1));
                Destination.Write(Unit(line, index));
                index += run;
                continue;
            }

            var literal = LiteralLength(line, index, units);
            if (literal == 1)
            {
                // A lone colour cannot be a literal run, because those start at two, and
                // "257 - 1" does not fit an octet. It is a repeat of one instead.
                Destination.WriteByte(0);
                Destination.Write(Unit(line, index));
                index++;
                continue;
            }

            // 2 to 128 non-repeating colours: "257 - count", then every colour.
            Destination.WriteByte((byte)(257 - literal));
            Destination.Write(line.Slice(index * BytesPerPixel, literal * BytesPerPixel));
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
        line.Slice(index * BytesPerPixel, BytesPerPixel);
}
