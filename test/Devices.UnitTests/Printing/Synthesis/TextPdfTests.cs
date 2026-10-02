using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Synthesis;
using AdaptArch.Devices.Rasterization;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Synthesis;

public partial class TextPdfTests
{
    // A4 in points.
    private const double Width = 595.28;
    private const double Height = 841.89;

    private static readonly PrintFont Liberation = PrintFont.FromBytes(RasterDocuments.FontProgramme());

    [Fact]
    public void Write_ShortText_IsOnePageInCourier()
    {
        var result = TextPdf.Write("Hello, world", Width, Height, []);

        Assert.Equal(1, result.PageCount);
        Assert.Equal(0, result.MissingCharacters);
        Assert.Contains("/BaseFont/Courier/Encoding/WinAnsiEncoding", Latin1(result.Pdf), StringComparison.Ordinal);
        Assert.Contains("(Hello, world) Tj", Contents(result.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_AFormFeed_StartsAPage() =>
        Assert.Equal(4, TextPdf.Write("one\ftwo\f\ffour", Width, Height, []).PageCount);

    [Fact]
    public void Write_ATrailingFormFeed_AddsNoBlankPage() =>
        Assert.Equal(1, TextPdf.Write("one page\f", Width, Height, []).PageCount);

    [Fact]
    public void Write_AFormFeedAfterAFullPage_AddsNoBlankPage()
    {
        // The last line of the first page ends with a newline, which already turned the
        // page; the form feed that follows must not turn it a second time.
        const int rows = (int)((Height - (2 * TextPdf.Margin)) / TextPdf.LineHeight);
        var text = String.Concat(Enumerable.Repeat("line\n", rows)) + "\fsecond";

        Assert.Equal(2, TextPdf.Write(text, Width, Height, []).PageCount);
    }

    [Fact]
    public void Write_AReturnNewlinePair_IsOneLineBreak() =>
        Assert.Equal(1, TextPdf.Write("one\r\ntwo\rthree", Width, Height, []).PageCount);

    [Fact]
    public void Write_MoreLinesThanAPageHolds_Overflows()
    {
        const int rows = (int)((Height - (2 * TextPdf.Margin)) / TextPdf.LineHeight);
        var text = String.Join('\n', Enumerable.Range(0, rows + 1).Select(static line => $"line {line}"));

        Assert.Equal(2, TextPdf.Write(text, Width, Height, []).PageCount);
    }

    [Fact]
    public void Write_ALongLine_WrapsAtTheMargin()
    {
        const int columns = (int)((Width - (2 * TextPdf.Margin)) / (TextPdf.FontSize * 0.6));

        var contents = Contents(TextPdf.Write(new string('x', columns + 3), Width, Height, []).Pdf);

        Assert.Contains($"({new string('x', columns)}) Tj", contents, StringComparison.Ordinal);
        Assert.Contains("(xxx) Tj", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ATab_MovesToTheNextStopOfEight()
    {
        var contents = Contents(TextPdf.Write("ab\tc", Width, Height, []).Pdf);

        // The c sits at column 8: the margin plus eight cells of six points.
        Assert.Contains($"{36 + (8 * 6)} ", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_Windows1252Characters_AreEscapedForCourier()
    {
        var contents = Contents(TextPdf.Write("café € (x)", Width, Height, []).Pdf);

        Assert.Contains(@"(caf\351 \200 \(x\)) Tj", contents, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TextThatLooksLikePdf_StaysInsideItsString()
    {
        var contents = Contents(TextPdf.Write(@"a\b \) Tj ET (", Width, Height, []).Pdf);

        Assert.Contains(@"(a\\b \\\) Tj ET \() Tj", contents, StringComparison.Ordinal);
        Assert.Single(TjOperator().Matches(contents));
    }

    [Fact]
    public void Write_ControlCharacters_NeverReachTheContentStream()
    {
        var contents = Contents(TextPdf.Write("a\u0000b\u001Bc\u007F", Width, Height, []).Pdf);

        Assert.Contains("(abc) Tj", contents, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001B', contents);
    }

    [Fact]
    public void Write_ACharacterNoFontCarries_IsCountedAndPrintedAsAQuestionMark()
    {
        var result = TextPdf.Write("Привет", Width, Height, []);

        Assert.Equal(6, result.MissingCharacters);
        Assert.Contains("(??????) Tj", Contents(result.Pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ACharacterAFallbackFontCarries_EmbedsThatFont()
    {
        var result = TextPdf.Write("Привет", Width, Height, [Liberation]);
        var pdf = Latin1(result.Pdf);

        Assert.Equal(0, result.MissingCharacters);
        Assert.Contains("/Subtype/CIDFontType2", pdf, StringComparison.Ordinal);
        Assert.Contains("/FontFile2", pdf, StringComparison.Ordinal);
        Assert.Contains($"/BaseFont/{Liberation.Name}", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_TextCourierCarries_EmbedsNoFont() =>
        Assert.DoesNotContain("/FontFile2", Latin1(TextPdf.Write("plain", Width, Height, [Liberation]).Pdf), StringComparison.Ordinal);

    [Fact]
    public void Write_TheDocument_HasAValidCrossReferenceTable()
    {
        var pdf = TextPdf.Write("a\fb", Width, Height, [Liberation]).Pdf;
        var text = Latin1(pdf);

        var start = Int32.Parse(StartXref().Match(text).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", text[start..], StringComparison.Ordinal);
        foreach (Match entry in XrefEntry().Matches(text[start..]))
        {
            var offset = Int32.Parse(entry.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Matches(@"^\d+ 0 obj", text[offset..]);
        }
    }

    internal static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    // Every Flate stream, inflated, as one text.
    internal static string Contents(byte[] pdf)
    {
        var text = Latin1(pdf);
        StringBuilder contents = new();
        foreach (Match match in FlateStream().Matches(text))
        {
            var start = match.Index + match.Length;
            var end = text.IndexOf("\nendstream", start, StringComparison.Ordinal);
            try
            {
                using ZLibStream zlib = new(new MemoryStream(pdf[start..end]), CompressionMode.Decompress);
                using StreamReader reader = new(zlib, Encoding.Latin1);
                _ = contents.Append(reader.ReadToEnd());
            }
            catch (InvalidDataException)
            {
                // A font programme or an image, which is not text.
            }
        }

        return contents.ToString();
    }

    [GeneratedRegex(@"/Filter/FlateDecode/Length \d+>>\nstream\n")]
    private static partial Regex FlateStream();

    // A Tj that ends a string, rather than one escaped inside it.
    [GeneratedRegex(@"(?<!\\)\) Tj")]
    private static partial Regex TjOperator();

    [GeneratedRegex(@"startxref\n(\d+)")]
    private static partial Regex StartXref();

    [GeneratedRegex(@"(\d{10}) 00000 n")]
    private static partial Regex XrefEntry();
}
