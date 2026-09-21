#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AdaptArch.Devices.Rasterization;

/// <summary>
/// The PDF the rasterization scenarios render.
/// </summary>
/// <remarks>
/// Built in code rather than checked in, for the reason <c>TestPdf</c> states: what a test
/// renders is then readable in the test, and nothing depends on a binary file.
/// <para>
/// Every mark on a page is there to be seen in the PNG a scenario writes. The colour band
/// says which page it is and that the channels did not swap; the corner block is in one
/// corner only, so a back side the printer reads upside down is obvious at a glance; and the
/// numeral says the same thing again for a person rather than for an assertion.
/// </para>
/// </remarks>
public static class RasterDocuments
{
    /// <summary>A4, in the points a PDF is authored in.</summary>
    public const double WidthPoints = 595;

    public const double HeightPoints = 842;

    /// <summary>How tall the colour band across the top of each page is, in points.</summary>
    public const double BandPoints = 100;

    /// <summary>How wide the black corner block is, in points.</summary>
    public const double CornerPoints = 60;

    /// <summary>The left edge of the barcode on every page, in points.</summary>
    public const double BarcodeLeftPoints = 300;

    /// <summary>The bottom edge of the barcode on every page, in points.</summary>
    public const double BarcodeBottomPoints = 150;

    /// <summary>
    /// The digits each page's barcode carries: the page number, in four digits.
    /// </summary>
    /// <param name="page">The 0-based page.</param>
    /// <returns>The digits.</returns>
    public static string BarcodeDigits(int page) => (page + 1).ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>
    /// The colour of each page's band, in red, green, blue order.
    /// </summary>
    /// <remarks>
    /// Saturated and all different in every channel, so a swapped pair of channels changes
    /// the answer rather than only the shade.
    /// </remarks>
    public static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)> BandColors =
    [
        (255, 0, 0),
        (0, 255, 0),
        (0, 0, 255),
        (255, 0, 255),
    ];

    /// <summary>The number of pages <see cref="FourPages"/> writes.</summary>
    public const int PageCount = 4;

    /// <summary>
    /// Four A4 pages, each with a coloured band along the top, a black block in the
    /// bottom-left corner, its own page number and a barcode carrying that number.
    /// </summary>
    public static byte[] FourPages()
    {
        // Latin1 and not ASCII, because the font programme below is binary and every octet
        // of it has to survive the round trip through this builder unchanged.
        List<byte> body = [];
        List<int> offsets = [];

        void Add(string obj)
        {
            offsets.Add(body.Count);
            body.AddRange(Encoding.Latin1.GetBytes(obj));
        }

        void AddStream(string dictionary, byte[] data)
        {
            offsets.Add(body.Count);
            body.AddRange(Encoding.Latin1.GetBytes($"{offsets.Count} 0 obj\n{dictionary}\nstream\n"));
            body.AddRange(data);
            body.AddRange(Encoding.Latin1.GetBytes("\nendstream\nendobj\n"));
        }

        body.AddRange(Encoding.Latin1.GetBytes("%PDF-1.4\n"));

        var kids = String.Join(' ', Enumerable.Range(0, PageCount).Select(page => $"{3 + (page * 2)} 0 R"));

        // The catalogue and the page tree are objects 1 and 2, each page takes two more, and
        // the three the font needs follow them.
        const int font = 3 + (PageCount * 2);
        const int descriptor = font + 1;
        const int programme = font + 2;

        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Add($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {PageCount} >>\nendobj\n");

        for (var page = 0; page < PageCount; page++)
        {
            var self = 3 + (page * 2);
            Add($"{self} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {WidthPoints} {HeightPoints}] "
                + $"/Contents {self + 1} 0 R /Resources << /Font << /F1 {font} 0 R >> >> >>\nendobj\n");

            var content = Content(page);
            Add($"{self + 1} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        }

        // The digits are the only characters drawn, and every digit of an Arial-metric font
        // is 556 thousandths of an em wide. The descriptor values are that same familiar set.
        // None of them decides a glyph shape: the embedded outlines do, which is the whole
        // reason the file below is in the repository.
        Add($"{font} 0 obj\n<< /Type /Font /Subtype /TrueType /BaseFont /{FontName} "
            + "/FirstChar 49 /LastChar 52 /Widths [556 556 556 556] /Encoding /WinAnsiEncoding "
            + $"/FontDescriptor {descriptor} 0 R >>\nendobj\n");

        Add($"{descriptor} 0 obj\n<< /Type /FontDescriptor /FontName /{FontName} /Flags 32 "
            + "/FontBBox [-543 -303 1301 980] /ItalicAngle 0 /Ascent 905 /Descent -212 "
            + $"/CapHeight 716 /StemV 88 /FontFile2 {programme} 0 R >>\nendobj\n");

        var outlines = FontProgramme();
        AddStream($"<< /Length {outlines.Length} /Length1 {outlines.Length} >>", outlines);

        var startXref = body.Count;
        StringBuilder tail = new();
        _ = tail.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        _ = tail.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            _ = tail.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        _ = tail.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref).Append("\n%%EOF\n");
        body.AddRange(Encoding.Latin1.GetBytes(tail.ToString()));

        return [.. body];
    }

    /// <summary>The name the embedded font programme is referred to by.</summary>
    public const string FontName = "LiberationSans";

    // Read out of the assembly rather than from beside it, so neither test project has to
    // copy a file to its output and no run depends on a working directory.
    private static byte[] FontProgramme()
    {
        using var stream = typeof(RasterDocuments).Assembly.GetManifestResourceStream(FontResource)
            ?? throw new InvalidOperationException(
                $"The assembly carries no '{FontResource}'. See test/Devices.TestSupport/Rasterization/fonts/README.md.");

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private const string FontResource = "LiberationSans-Regular.ttf";

    /// <summary>The width of <see cref="Label"/> in points, which is four inches.</summary>
    public const double LabelWidthPoints = 288;

    /// <summary>The height of <see cref="Label"/> in points, which is six inches.</summary>
    public const double LabelHeightPoints = 432;

    /// <summary>
    /// The sample shipping label: four inches by six, so a page genuinely smaller than the
    /// media it is placed on.
    /// </summary>
    /// <remarks>
    /// The one document here that is not built in code. The placement scenarios need a page
    /// the media is larger than, and scaling the A4 fixture down to that size is not the
    /// same test: a narrow bar of this fixture is 3.1 pixels at the scenario resolution, so
    /// half of one is under two, and what the scenario would then measure is the resampler
    /// rather than the placement. This is the label the samples print, embedded rather than
    /// copied so the two cannot drift and no run depends on a file beside the assembly.
    /// </remarks>
    public static byte[] Label()
    {
        using var stream = typeof(RasterDocuments).Assembly.GetManifestResourceStream(LabelResource)
            ?? throw new InvalidOperationException($"The assembly carries no '{LabelResource}'.");

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private const string LabelResource = "document.pdf";

    // PDF user space has its origin at the bottom-left, so the band is drawn at the top of
    // the sheet and the corner block at the bottom.
    private static string Content(int page)
    {
        (var red, var green, var blue) = BandColors[page];
        var colour = String.Create(
            CultureInfo.InvariantCulture,
            $"{red / 255.0:0.###} {green / 255.0:0.###} {blue / 255.0:0.###} rg");

        return String.Create(
            CultureInfo.InvariantCulture,
            $"{colour} 0 {HeightPoints - BandPoints} {WidthPoints} {BandPoints} re f\n"
            + $"0 0 0 rg 0 0 {CornerPoints} {CornerPoints} re f\n"
            + $"BT /F1 300 Tf 200 350 Td ({page + 1}) Tj ET\n")
            + Barcode.Draw(BarcodeDigits(page), BarcodeLeftPoints, BarcodeBottomPoints);
    }
}
