using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Synthesis;
using AdaptArch.Devices.Rasterization;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Synthesis;

public class PrintFontTests
{
    [Fact]
    public void FromBytes_ATrueTypeFont_ReadsItsNameAndGlyphs()
    {
        var font = PrintFont.FromBytes(RasterDocuments.FontProgramme());

        Assert.Equal("LiberationSans", font.Name);
        Assert.True(font.Font.TryGetGlyph('Ж', out var glyph));
        Assert.True(font.Font.AdvanceOf(glyph) > 0);
        Assert.False(font.Font.TryGetGlyph(0x4E2D, out _));
    }

    [Fact]
    public void FromBytes_AFontWithCffOutlines_IsRefused()
    {
        var error = Assert.Throws<ArgumentException>(() => PrintFont.FromBytes(Encoding.ASCII.GetBytes("OTTO\0\0\0\0")));

        Assert.Contains("CFF", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromBytes_NotAFont_IsRefused() =>
        Assert.Throws<ArgumentException>(() => PrintFont.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6 }));

    [Fact]
    public void TextFontsFor_TheJobList_ReplacesEveryOther()
    {
        var mine = PrintFont.FromBytes(RasterDocuments.FontProgramme());
        PrintFormatPolicy policy = new(null, null) { TextFonts = [mine] };

        Assert.Empty(policy.TextFontsFor([]));
        Assert.Equal([mine], policy.TextFontsFor(null));
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, "A")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, "A")]
    [InlineData(new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 }, "café")]
    [InlineData(new byte[] { 0x63, 0x61, 0x66, 0xE9, 0x80 }, "café€")]
    public void Decode_ReadsTheBomUtf8OrWindows1252(byte[] data, string expected) =>
        Assert.Equal(expected, DocumentSynthesis.Decode(data));
}
