using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AdaptArch.Devices.Printing.Synthesis;

// The smallest PDF 1.4 writer the synthesized documents need: numbered objects, streams and
// a cross-reference table. Objects may be written in any order once their numbers are reserved.
internal sealed class PdfWriter
{
    private readonly MemoryStream _output = new();
    private readonly List<long> _offsets = [0];

    public PdfWriter() => Write("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

    public int Reserve()
    {
        _offsets.Add(-1);
        return _offsets.Count - 1;
    }

    public void Object(int id, string body)
    {
        _offsets[id] = _output.Position;
        Write($"{id} 0 obj\n{body}\nendobj\n");
    }

    public void Stream(int id, string dictionary, ReadOnlySpan<byte> data)
    {
        _offsets[id] = _output.Position;
        Write($"{id} 0 obj\n<<{dictionary}/Length {data.Length}>>\nstream\n");
        _output.Write(data);
        Write("\nendstream\nendobj\n");
    }

    public void CompressedStream(int id, string dictionary, ReadOnlySpan<byte> data) =>
        Stream(id, $"{dictionary}/Filter/FlateDecode", Deflate(data));

    public byte[] Finish(int catalogId)
    {
        var start = _output.Position;
        StringBuilder table = new($"xref\n0 {_offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in _offsets.Skip(1))
        {
            _ = table.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        _ = table.Append(CultureInfo.InvariantCulture, $"trailer\n<</Size {_offsets.Count}/Root {catalogId} 0 R>>\nstartxref\n{start}\n%%EOF\n");
        Write(table.ToString());
        return _output.ToArray();
    }

    public static string Number(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    public static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }

    private void Write(string text) => _output.Write(Encoding.Latin1.GetBytes(text));
}
