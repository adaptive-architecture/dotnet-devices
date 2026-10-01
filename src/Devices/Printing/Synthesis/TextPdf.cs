using System.Globalization;
using System.Text;

namespace AdaptArch.Devices.Printing.Synthesis;

// Lays out monospaced text on pages of the media and writes it as PDF. Courier, one of the
// fonts every PDF reader carries, draws the Windows-1252 characters; any other character is
// drawn with the first embedded font that has it, in the cells nearest its advance, and
// stretched or narrowed to fill them, so that the columns stay aligned.
internal static class TextPdf
{
    public const double FontSize = 10;
    public const double LineHeight = 12;
    public const double Margin = 36;
    public const int TabSize = 8;

    // Courier advances 600 thousandths of an em, at every glyph.
    private const double Cell = FontSize * 0.6;

    public static TextPdfResult Write(string text, double pageWidth, double pageHeight, IReadOnlyList<PrintFont> fonts)
    {
        var columns = Math.Max(1, (int)((pageWidth - (2 * Margin)) / Cell));
        var rows = Math.Max(1, (int)((pageHeight - (2 * Margin)) / LineHeight));
        var layout = Layout(text, columns, rows, fonts);

        PdfWriter pdf = new();
        var catalog = pdf.Reserve();
        var pages = pdf.Reserve();
        var courier = pdf.Reserve();
        var embedded = layout.UsedFonts.Keys.Order().ToDictionary(static index => index, _ => pdf.Reserve());

        List<int> pageIds = [];
        var resources = Resources(courier, embedded);
        foreach (var page in layout.Pages)
        {
            var content = pdf.Reserve();
            pdf.CompressedStream(content, String.Empty, Content(page, pageHeight));
            var id = pdf.Reserve();
            pdf.Object(id, $"<</Type/Page/Parent {pages} 0 R/MediaBox[0 0 {PdfWriter.Number(pageWidth)} {PdfWriter.Number(pageHeight)}]/Resources{resources}/Contents {content} 0 R>>");
            pageIds.Add(id);
        }

        pdf.Object(courier, "<</Type/Font/Subtype/Type1/BaseFont/Courier/Encoding/WinAnsiEncoding>>");
        foreach ((var index, var id) in embedded)
        {
            WriteFont(pdf, id, fonts[index].Font, layout.UsedFonts[index]);
        }

        pdf.Object(pages, $"<</Type/Pages/Kids[{String.Join(' ', pageIds.Select(static id => $"{id} 0 R"))}]/Count {pageIds.Count}>>");
        pdf.Object(catalog, $"<</Type/Catalog/Pages {pages} 0 R>>");
        return new TextPdfResult(pdf.Finish(catalog), layout.Pages.Count, layout.Missing);
    }

    private static TextLayout Layout(string text, int columns, int rows, IReadOnlyList<PrintFont> fonts)
    {
        LayoutWriter writer = new(columns, rows);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                writer.BreakLine();
            }
            else if (rune.Value == '\f')
            {
                writer.BreakPage();
            }
            else if (rune.Value == '\t')
            {
                writer.Tab();
            }
            else if (rune.Value == ' ' || rune.Value == 0xA0)
            {
                writer.Place(new TextGlyph(-1, 0, 1, 0));
            }
            else if (!Rune.IsControl(rune))
            {
                writer.Place(GlyphFor(rune, fonts, writer.Result));
            }
        }

        return writer.Finish();
    }

    private static TextGlyph GlyphFor(Rune rune, IReadOnlyList<PrintFont> fonts, TextLayout layout)
    {
        if (WinAnsi.TryEncode(rune.Value, out var code))
        {
            return new TextGlyph(-1, code, 1, 0);
        }

        for (var index = 0; index < fonts.Count; index++)
        {
            var font = fonts[index].Font;
            if (font.TryGetGlyph(rune.Value, out var glyph))
            {
                var width = font.Scaled(font.AdvanceOf(glyph));
                if (!layout.UsedFonts.TryGetValue(index, out var used))
                {
                    used = [];
                    layout.UsedFonts[index] = used;
                }

                used[glyph] = width;
                var cells = Math.Max(1, (int)Math.Round(width * FontSize / 1000.0 / Cell));
                return new TextGlyph(index, glyph, cells, width);
            }
        }

        layout.Missing++;
        return new TextGlyph(-1, '?', 1, 0);
    }

    private static string Resources(int courier, Dictionary<int, int> embedded)
    {
        StringBuilder fonts = new($"<</Font<</F0 {courier} 0 R");
        foreach ((var index, var id) in embedded)
        {
            _ = fonts.Append(CultureInfo.InvariantCulture, $"/F{index + 1} {id} 0 R");
        }

        return fonts.Append(">>>>").ToString();
    }

    private static byte[] Content(List<TextLine> page, double pageHeight)
    {
        StringBuilder content = new("BT\n");
        var baseline = pageHeight - Margin - FontSize;
        foreach (var line in page)
        {
            foreach (var run in Runs(line.Glyphs))
            {
                var x = Margin + (run[0].Column * Cell);
                var stretch = run[0].Font < 0 || run[0].Width == 0 ? 100 : run[0].Cells * Cell * 100000.0 / (run[0].Width * FontSize);
                _ = content.Append(CultureInfo.InvariantCulture, $"/F{run[0].Font + 1} {PdfWriter.Number(FontSize)} Tf {PdfWriter.Number(stretch)} Tz 1 0 0 1 {PdfWriter.Number(x)} {PdfWriter.Number(baseline)} Tm ");
                _ = run[0].Font < 0 ? content.Append(Literal(run)) : content.Append(Hex(run));
                _ = content.Append(" Tj\n");
            }

            baseline -= LineHeight;
        }

        return Encoding.Latin1.GetBytes(content.Append("ET\n").ToString());
    }

    // Courier advances exactly one cell, so a run of it is one string. An embedded glyph sits
    // at its own cell, because its advance is not one.
    private static IEnumerable<List<TextGlyph>> Runs(List<TextGlyph> glyphs)
    {
        List<TextGlyph> run = [];
        foreach (var glyph in glyphs)
        {
            var joins = run.Count > 0 && run[0].Font < 0 && glyph.Font < 0 && glyph.Column == run[^1].Column + 1;
            if (!joins && run.Count > 0)
            {
                yield return run;
                run = [];
            }

            run.Add(glyph);
        }

        if (run.Count > 0)
        {
            yield return run;
        }
    }

    private static string Literal(List<TextGlyph> run)
    {
        StringBuilder literal = new("(");
        foreach (var value in run.Select(static glyph => glyph.Code))
        {
            var code = value == 0 ? ' ' : (char)value;
            if (code is '(' or ')' or '\\')
            {
                _ = literal.Append('\\').Append(code);
            }
            else if (code > 126)
            {
                _ = literal.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
            }
            else
            {
                _ = literal.Append(code);
            }
        }

        return literal.Append(')').ToString();
    }

    private static string Hex(List<TextGlyph> run) =>
        $"<{String.Concat(run.Select(static glyph => glyph.Code.ToString("X4", CultureInfo.InvariantCulture)))}>";

    private static void WriteFont(PdfWriter pdf, int id, TrueTypeFont font, Dictionary<ushort, int> glyphs)
    {
        var descendant = pdf.Reserve();
        var descriptor = pdf.Reserve();
        var file = pdf.Reserve();
        var widths = String.Join(' ', glyphs.OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key}[{pair.Value}]"));
        var box = String.Join(' ', font.BoundingBox.Select(value => font.Scaled(value)));

        pdf.Object(id, $"<</Type/Font/Subtype/Type0/BaseFont/{font.Name}/Encoding/Identity-H/DescendantFonts[{descendant} 0 R]>>");
        pdf.Object(descendant, $"<</Type/Font/Subtype/CIDFontType2/BaseFont/{font.Name}/CIDSystemInfo<</Registry(Adobe)/Ordering(Identity)/Supplement 0>>/FontDescriptor {descriptor} 0 R/CIDToGIDMap/Identity/W[{widths}]>>");
        pdf.Object(descriptor, $"<</Type/FontDescriptor/FontName/{font.Name}/Flags 4/FontBBox[{box}]/ItalicAngle 0/Ascent {font.Scaled(font.Ascent)}/Descent {font.Scaled(font.Descent)}/CapHeight {font.Scaled(font.Ascent)}/StemV 80/FontFile2 {file} 0 R>>");
        pdf.CompressedStream(file, $"/Length1 {font.Data.Length}", font.Data);
    }

    // Fills lines and pages cell by cell, wrapping at the right margin and overflowing at the bottom.
    private sealed class LayoutWriter
    {
        private readonly int _columns;
        private readonly int _rows;
        private List<TextLine> _page = [];
        private TextLine _line = new();
        private int _column;

        public LayoutWriter(int columns, int rows)
        {
            _columns = columns;
            _rows = rows;
        }

        public TextLayout Result { get; } = new();

        public void BreakLine()
        {
            _page.Add(_line);
            _line = new TextLine();
            _column = 0;
            if (_page.Count == _rows)
            {
                Result.Pages.Add(_page);
                _page = [];
            }
        }

        public void BreakPage()
        {
            if (_line.Glyphs.Count > 0)
            {
                _page.Add(_line);
            }

            _line = new TextLine();
            _column = 0;
            Result.Pages.Add(_page);
            _page = [];
        }

        public void Tab() => _column = Math.Min(_columns, _column + TabSize - (_column % TabSize));

        public void Place(TextGlyph glyph)
        {
            if (_column + glyph.Cells > _columns && _column > 0)
            {
                BreakLine();
            }

            _line.Glyphs.Add(glyph with { Column = _column });
            _column += glyph.Cells;
        }

        public TextLayout Finish()
        {
            if (_line.Glyphs.Count > 0)
            {
                _page.Add(_line);
            }

            if (_page.Count > 0 || Result.Pages.Count == 0)
            {
                Result.Pages.Add(_page);
            }

            return Result;
        }
    }

    private sealed class TextLayout
    {
        public List<List<TextLine>> Pages { get; } = [];

        public Dictionary<int, Dictionary<ushort, int>> UsedFonts { get; } = [];

        public int Missing { get; set; }
    }

    private sealed class TextLine
    {
        public List<TextGlyph> Glyphs { get; } = [];
    }

    // Font -1 is Courier, whose code is a Windows-1252 byte; any other font is embedded and
    // its code is a glyph index.
    private sealed record TextGlyph(int Font, int Code, int Cells, int Width)
    {
        public int Column { get; init; }
    }
}

// The document, how many pages it has, and how many characters no font could draw.
internal sealed record TextPdfResult(byte[] Pdf, int PageCount, int MissingCharacters);
