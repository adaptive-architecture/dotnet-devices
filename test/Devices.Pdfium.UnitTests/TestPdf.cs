#nullable enable
using System.Text;

namespace AdaptArch.Devices.Pdfium.UnitTests;

/// <summary>
/// PDFs built in code rather than checked in, so what a test renders is readable in the
/// test and nothing depends on a binary file.
/// </summary>
internal static class TestPdf
{
    /// <summary>
    /// One page of one inch square, filled with pure red.
    /// </summary>
    /// <remarks>
    /// PDFium writes blue, green, red, and a page rendered onto white stays white whether
    /// the channels are swapped or not. A page that is one saturated colour is what tells
    /// the two apart.
    /// </remarks>
    public static byte[] RedSquare() => Build("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 72 72] /Contents 4 0 R >>", "1 0 0 rg 0 0 72 72 re f");

    /// <summary>US Letter, 612 by 792 points, with a line of text on each page.</summary>
    public static byte[] WithPages(int pageCount)
    {
        StringBuilder body = new();
        _ = body.Append("%PDF-1.4\n");

        List<int> offsets = [];
        void Add(string obj)
        {
            offsets.Add(body.Length);
            _ = body.Append(obj);
        }

        // Objects 1 and 2 are the catalogue and the page tree; each page then takes two
        // objects, and the font is the last one.
        var kids = String.Join(' ', Enumerable.Range(0, pageCount).Select(page => $"{3 + (page * 2)} 0 R"));
        var font = 3 + (pageCount * 2);

        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Add($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n");

        for (var page = 0; page < pageCount; page++)
        {
            var self = 3 + (page * 2);
            Add($"{self} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {self + 1} 0 R "
                + $"/Resources << /Font << /F1 {font} 0 R >> >> >>\nendobj\n");

            var content = $"BT /F1 48 Tf 72 700 Td (page {page + 1}) Tj ET";
            Add($"{self + 1} 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        }

        Add($"{font} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        var startXref = body.Length;
        _ = body.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        _ = body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            _ = body.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        _ = body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(body.ToString());
    }

    // The four-object form: catalogue, page tree, one page, one content stream.
    private static byte[] Build(string page, string content)
    {
        StringBuilder body = new();
        _ = body.Append("%PDF-1.4\n");

        List<int> offsets = [];
        void Add(string obj)
        {
            offsets.Add(body.Length);
            _ = body.Append(obj);
        }

        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Add("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        Add($"3 0 obj\n{page}\nendobj\n");
        Add($"4 0 obj\n<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");

        var startXref = body.Length;
        _ = body.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        _ = body.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            _ = body.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        _ = body.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(startXref).Append("\n%%EOF\n");

        return Encoding.ASCII.GetBytes(body.ToString());
    }
}
