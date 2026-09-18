#nullable enable
using System.Text;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// Documents built in code rather than checked in, so what a test sends is readable in the
/// test and nothing depends on a binary file.
/// </summary>
internal static class TestDocuments
{
    /// <summary>
    /// A one-page PDF, the smallest that is still structurally valid: a printer that reads
    /// PDF accepts it, and one that does not has something real to refuse.
    /// </summary>
    public static byte[] OnePagePdf()
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
        Add("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj\n");

        const string Stream = "BT /F1 24 Tf 72 700 Td (dotnet-devices integration test) Tj ET";
        Add($"4 0 obj\n<< /Length {Stream.Length} >>\nstream\n{Stream}\nendstream\nendobj\n");
        Add("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

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

    /// <summary>A label in ZPL, which every printer language test sends unchanged.</summary>
    public const string ZplLabel = "^XA^FO50,50^ADN,36,20^FDdotnet-devices^FS^XZ";
}
