#nullable enable
using AdaptArch.Devices.Pdf;

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

    /// <summary>
    /// One page of four inches by two, with a barcode on it and nothing else.
    /// </summary>
    /// <remarks>
    /// The size is label stock, and the symbol is what a test measures: its bars have known
    /// widths, so how sharp an edge is and where the first bar landed are both arithmetic.
    /// <see cref="Rasterization.Barcode"/> says why it is Interleaved 2 of 5.
    /// </remarks>
    public static byte[] Barcode(string digits = "0042")
    {
        // 288 by 144 points is 4 by 2 inches, the commonest shipping label.
        var content = Rasterization.Barcode.Draw(digits, BarcodeLeftPoints, BarcodeBottomPoints);
        return Build("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 288 144] /Contents 4 0 R >>", content);
    }

    /// <summary>The left edge of the barcode <see cref="Barcode"/> draws, in points.</summary>
    public const double BarcodeLeftPoints = 24;

    /// <summary>The bottom edge of the barcode <see cref="Barcode"/> draws, in points.</summary>
    public const double BarcodeBottomPoints = 36;

    /// <summary>US Letter, 612 by 792 points, with a line of text on each page.</summary>
    public static byte[] WithPages(int pageCount)
    {
        // Objects 1 and 2 are the catalogue and the page tree; each page then takes two
        // objects, and the font is the last one.
        var kids = String.Join(' ', Enumerable.Range(0, pageCount).Select(page => $"{3 + (page * 2)} 0 R"));
        var font = 3 + (pageCount * 2);

        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>",
        ];

        for (var page = 0; page < pageCount; page++)
        {
            var self = 3 + (page * 2);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents {self + 1} 0 R "
                + $"/Resources << /Font << /F1 {font} 0 R >> >> >>");
            objects.Add(MinimalPdf.Stream($"BT /F1 48 Tf 72 700 Td (page {page + 1}) Tj ET"));
        }

        objects.Add(MinimalPdf.Helvetica);

        return MinimalPdf.From(objects);
    }

    // The four-object form: catalogue, page tree, one page, one content stream.
    private static byte[] Build(string page, string content) =>
        MinimalPdf.From([
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            page,
            MinimalPdf.Stream(content),
        ]);
}
