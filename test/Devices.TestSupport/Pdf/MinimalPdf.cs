#nullable enable
using System.Collections.Generic;
using System.Text;

namespace AdaptArch.Devices.Pdf;

/// <summary>
/// Assembles a PDF file around a list of objects: the header, the cross-reference table
/// with each object's offset, and the trailer.
/// </summary>
/// <remarks>
/// Shared so that a test project builds its documents in code rather than depending on a
/// checked-in binary, and so that the file structure — the part no test is about — is
/// written once. A caller supplies the objects and nothing else.
/// </remarks>
public static class MinimalPdf
{
    /// <summary>
    /// Wraps the objects, numbered 1 upwards in the order given, into a file whose root is
    /// object 1.
    /// </summary>
    /// <param name="objects">Each object's body, without its <c>N 0 obj</c> and <c>endobj</c>.</param>
    /// <returns>The file.</returns>
    public static byte[] From(IEnumerable<string> objects)
    {
        StringBuilder body = new();
        _ = body.Append("%PDF-1.4\n");

        List<int> offsets = [];
        foreach (var content in objects)
        {
            offsets.Add(body.Length);
            _ = body.Append(offsets.Count).Append(" 0 obj\n").Append(content).Append("\nendobj\n");
        }

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

    /// <summary>
    /// A content stream object, whose <c>/Length</c> has to match what it carries.
    /// </summary>
    /// <param name="content">The operators.</param>
    /// <returns>The object body.</returns>
    public static string Stream(string content) =>
        $"<< /Length {content.Length} >>\nstream\n{content}\nendstream";

    /// <summary>The standard Type 1 font object every page here names as <c>/F1</c>.</summary>
    public const string Helvetica = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>";
}
