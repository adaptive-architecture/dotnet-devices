using System.Buffers.Binary;
using System.Text;

namespace AdaptArch.Devices.Printing.Synthesis;

// What embedding a TrueType font in a PDF needs from it: the glyph of a character, the
// advance of a glyph, and the metrics of the font descriptor. Read once, when the font is given.
internal sealed class TrueTypeFont
{
    private const ushort RestrictedLicenseEmbedding = 0x0002;

    private readonly Dictionary<int, ushort> _glyphs;
    private readonly ushort[] _advances;

    private TrueTypeFont(byte[] data)
    {
        Data = data;
        var tables = ReadTables(data);

        var head = Table(tables, "head");
        UnitsPerEm = U16(head, 18);
        BoundingBox = [S16(head, 36), S16(head, 38), S16(head, 40), S16(head, 42)];

        var hhea = Table(tables, "hhea");
        Ascent = S16(hhea, 4);
        Descent = S16(hhea, 6);
        var metricCount = U16(hhea, 34);

        var glyphCount = U16(Table(tables, "maxp"), 4);
        _advances = ReadAdvances(Table(tables, "hmtx"), metricCount, glyphCount);
        _glyphs = ReadCmap(Table(tables, "cmap"));

        if (tables.TryGetValue("OS/2", out var os2) && os2.Length >= 10
            && (U16(os2.Span, 8) & 0x000F) == RestrictedLicenseEmbedding)
        {
            throw new ArgumentException("The font's licence forbids embedding it (OS/2 fsType is restricted).", nameof(data));
        }

        Name = ReadPostScriptName(tables) ?? "EmbeddedFont";
    }

    public byte[] Data { get; }

    public string Name { get; }

    public int UnitsPerEm { get; }

    public int Ascent { get; }

    public int Descent { get; }

    public short[] BoundingBox { get; }

    public static TrueTypeFont Read(byte[] data)
    {
        try
        {
            return new TrueTypeFont(data);
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentOutOfRangeException or KeyNotFoundException)
        {
            throw new ArgumentException($"The font is not a TrueType font the library can read: {exception.Message}", nameof(data), exception);
        }
    }

    public bool TryGetGlyph(int codePoint, out ushort glyph) =>
        _glyphs.TryGetValue(codePoint, out glyph) && glyph != 0;

    public int AdvanceOf(ushort glyph) => _advances[Math.Min(glyph, _advances.Length - 1)];

    // In the thousandths of an em that PDF measures glyph space in.
    public int Scaled(int units) => (int)Math.Round(units * 1000.0 / UnitsPerEm);

    private static Dictionary<string, ReadOnlyMemory<byte>> ReadTables(byte[] data)
    {
        var version = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (version == 0x74746366)
        {
            throw new ArgumentException("A TrueType collection (.ttc) cannot be embedded; give one font of it.", nameof(data));
        }

        if (version == 0x4F54544F)
        {
            throw new ArgumentException("The font has CFF (PostScript) outlines; only TrueType outlines can be embedded.", nameof(data));
        }

        if (version is not 0x00010000 and not 0x74727565)
        {
            throw new ArgumentException("The data is not a TrueType font.", nameof(data));
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        Dictionary<string, ReadOnlyMemory<byte>> tables = new(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var record = 12 + (index * 16);
            var tag = Encoding.ASCII.GetString(data, record, 4);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
            tables[tag] = data.AsMemory(offset, length);
        }

        return tables;
    }

    private static ReadOnlySpan<byte> Table(Dictionary<string, ReadOnlyMemory<byte>> tables, string tag) =>
        tables.TryGetValue(tag, out var table)
            ? table.Span
            : throw new KeyNotFoundException($"it has no '{tag}' table");

    private static ushort[] ReadAdvances(ReadOnlySpan<byte> hmtx, int metricCount, int glyphCount)
    {
        var advances = new ushort[Math.Max(1, Math.Max(metricCount, glyphCount))];
        for (var glyph = 0; glyph < advances.Length; glyph++)
        {
            advances[glyph] = glyph < metricCount ? U16(hmtx, glyph * 4) : advances[metricCount - 1];
        }

        return advances;
    }

    // Format 12 covers every plane and format 4 the basic one; the first Unicode table of
    // either kind is enough.
    private static Dictionary<int, ushort> ReadCmap(ReadOnlySpan<byte> cmap)
    {
        var count = U16(cmap, 2);
        var best = -1;
        for (var index = 0; index < count; index++)
        {
            var record = 4 + (index * 8);
            var platform = U16(cmap, record);
            var encoding = U16(cmap, record + 2);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(record + 4)..]);
            var unicode = platform == 0 || (platform == 3 && encoding is 1 or 10);
            if (!unicode)
            {
                continue;
            }

            var format = U16(cmap, offset);
            if (format == 12)
            {
                return ReadFormat12(cmap[offset..]);
            }

            if (format == 4 && best < 0)
            {
                best = offset;
            }
        }

        return best >= 0 ? ReadFormat4(cmap[best..]) : throw new KeyNotFoundException("it has no Unicode character map");
    }

    private static Dictionary<int, ushort> ReadFormat4(ReadOnlySpan<byte> table)
    {
        var segments = U16(table, 6) / 2;
        const int ends = 14;
        var starts = ends + (segments * 2) + 2;
        var deltas = starts + (segments * 2);
        var ranges = deltas + (segments * 2);
        Dictionary<int, ushort> glyphs = [];
        for (var segment = 0; segment < segments; segment++)
        {
            var end = U16(table, ends + (segment * 2));
            var start = U16(table, starts + (segment * 2));
            var delta = S16(table, deltas + (segment * 2));
            var rangeOffset = U16(table, ranges + (segment * 2));
            for (var code = start; code <= end && code != 0xFFFF; code++)
            {
                int glyph;
                if (rangeOffset == 0)
                {
                    glyph = (code + delta) & 0xFFFF;
                }
                else
                {
                    var at = ranges + (segment * 2) + rangeOffset + ((code - start) * 2);
                    glyph = U16(table, at);
                    glyph = glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
                }

                glyphs[code] = (ushort)glyph;
            }
        }

        return glyphs;
    }

    private static Dictionary<int, ushort> ReadFormat12(ReadOnlySpan<byte> table)
    {
        var groups = (int)BinaryPrimitives.ReadUInt32BigEndian(table[12..]);
        Dictionary<int, ushort> glyphs = [];
        for (var group = 0; group < groups; group++)
        {
            var record = table[(16 + (group * 12))..];
            var start = (int)BinaryPrimitives.ReadUInt32BigEndian(record);
            var end = (int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
            var glyph = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            for (var code = start; code <= end; code++)
            {
                glyphs[code] = (ushort)(glyph + code - start);
            }
        }

        return glyphs;
    }

    // Name 6, the PostScript name, is what a PDF names the font by.
    private static string? ReadPostScriptName(Dictionary<string, ReadOnlyMemory<byte>> tables)
    {
        if (!tables.TryGetValue("name", out var memory))
        {
            return null;
        }

        var name = memory.Span;
        var count = U16(name, 2);
        var strings = U16(name, 4);
        for (var index = 0; index < count; index++)
        {
            var record = 6 + (index * 12);
            if (U16(name, record + 6) != 6)
            {
                continue;
            }

            var platform = U16(name, record);
            var length = U16(name, record + 8);
            var offset = strings + U16(name, record + 10);
            var bytes = name.Slice(offset, length);
            var text = platform is 0 or 3 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            var clean = new string([.. text.Where(static character => Char.IsAsciiLetterOrDigit(character) || character is '-' or '_')]);
            if (clean.Length > 0)
            {
                return clean;
            }
        }

        return null;
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static short S16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
}
