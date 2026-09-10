using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterIdSyntaxTests
{
    [Theory]
    [InlineData("EPSON_L6270", "EPSON_L6270")]
    [InlineData("Front Desk", "Front%20Desk")]
    [InlineData("\\\\server\\queue", "%5C%5Cserver%5Cqueue")]
    [InlineData("a:b", "a%3Ab")]
    [InlineData("Büro", "B%C3%BCro")]
    public void Encode_EscapesEverythingOutsideTheUnreservedSet(string value, string expected) =>
        Assert.Equal(expected, PrinterIdSyntax.Encode(value));

    [Theory]
    [InlineData("EPSON_L6270", "EPSON_L6270")]
    [InlineData("Front%20Desk", "Front Desk")]
    [InlineData("%5C%5Cserver%5Cqueue", "\\\\server\\queue")]
    [InlineData("%c3%bc", "ü")]
    public void TryDecode_ReadsAnEscapedValue(string text, string expected)
    {
        Assert.True(PrinterIdSyntax.TryDecode(text, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void TryDecode_AcceptsACharacterAnEncoderWouldHaveEscaped()
    {
        Assert.True(PrinterIdSyntax.TryDecode("Front Desk", out var value));
        Assert.Equal("Front Desk", value);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("%2")]
    [InlineData("%zz")]
    [InlineData("%2F")]
    [InlineData("%3F")]
    [InlineData("%23")]
    [InlineData("%00")]
    [InlineData("%c3")]
    [InlineData("")]
    public void TryDecode_RefusesABrokenOrForbiddenValue(string text) =>
        Assert.False(PrinterIdSyntax.TryDecode(text, out _));

    [Fact]
    public void EncodeThenDecode_RoundTrips()
    {
        const string Name = "\\\\srv\\HP LaserJet 4 (Büro)";
        Assert.True(PrinterIdSyntax.TryDecode(PrinterIdSyntax.Encode(Name), out var value));
        Assert.Equal(Name, value);
    }

    [Theory]
    [InlineData("192.168.1.5", "192.168.1.5", 0)]
    [InlineData("192.168.1.5:9101", "192.168.1.5", 9101)]
    [InlineData("printer.local", "printer.local", 0)]
    [InlineData("[2001:db8::5]", "2001:db8::5", 0)]
    [InlineData("[2001:db8::5]:8631", "2001:db8::5", 8631)]
    public void TrySplitHostPort_ReadsTheHostAndThePort(string authority, string expectedHost, int expectedPort)
    {
        Assert.True(PrinterIdSyntax.TrySplitHostPort(authority, out var host, out var port));
        Assert.Equal(expectedHost, host);
        Assert.Equal(expectedPort, port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2001:db8::5")]
    [InlineData("[2001:db8::5")]
    [InlineData("[2001:db8::5]x")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:abc")]
    [InlineData("user@host")]
    [InlineData(":9100")]
    public void TrySplitHostPort_RefusesAMalformedAuthority(string authority) =>
        Assert.False(PrinterIdSyntax.TrySplitHostPort(authority, out _, out _));

    [Theory]
    [InlineData("192.168.1.5", "192.168.1.5")]
    [InlineData("printer.local", "printer.local")]
    [InlineData("2001:db8::5", "[2001:db8::5]")]
    public void FormatHost_BracketsOnlyAnIPv6Literal(string host, string expected) =>
        Assert.Equal(expected, PrinterIdSyntax.FormatHost(host));

    [Theory]
    [InlineData("a/b", true)]
    [InlineData("a?b", true)]
    [InlineData("a#b", true)]
    [InlineData("a-b", false)]
    public void ContainsReservedDelimiter_FindsWhatEndsAnAuthority(string text, bool expected) =>
        Assert.Equal(expected, PrinterIdSyntax.ContainsReservedDelimiter(text));
}
