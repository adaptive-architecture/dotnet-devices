#nullable enable
using System.Collections.Generic;
using System.Globalization;
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
internal static class RasterDocuments
{
    /// <summary>A4, in the points a PDF is authored in.</summary>
    internal const double WidthPoints = 595;

    internal const double HeightPoints = 842;

    /// <summary>How tall the colour band across the top of each page is, in points.</summary>
    internal const double BandPoints = 100;

    /// <summary>How wide the black corner block is, in points.</summary>
    internal const double CornerPoints = 60;

    /// <summary>
    /// The colour of each page's band, in red, green, blue order.
    /// </summary>
    /// <remarks>
    /// Saturated and all different in every channel, so a swapped pair of channels changes
    /// the answer rather than only the shade.
    /// </remarks>
    internal static readonly IReadOnlyList<(byte Red, byte Green, byte Blue)> BandColors =
    [
        (255, 0, 0),
        (0, 255, 0),
        (0, 0, 255),
        (255, 0, 255),
    ];

    /// <summary>The number of pages <see cref="FourPages"/> writes.</summary>
    internal const int PageCount = 4;

    /// <summary>
    /// Four A4 pages, each with a coloured band along the top, a black block in the
    /// bottom-left corner and its own page number.
    /// </summary>
    internal static byte[] FourPages()
    {
        StringBuilder body = new();
        _ = body.Append("%PDF-1.4\n");

        List<int> offsets = [];
        void Add(string obj)
        {
            offsets.Add(body.Length);
            _ = body.Append(obj);
        }

        var kids = String.Join(' ', Enumerable.Range(0, PageCount).Select(page => $"{3 + (page * 2)} 0 R"));
        var font = 3 + (PageCount * 2);

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

        Add($"{font} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var startXref = body.Length;
        _ = body.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        _ = body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            _ = body.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        _ = body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(body.ToString());
    }

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
            + $"BT /F1 300 Tf 200 350 Td ({page + 1}) Tj ET");
    }
}
