using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintFormatPolicyTests
{
    private sealed class StubConverter(string contentType) : IPrintPayloadConverter
    {
        public string Name { get; } = contentType;

        public bool CanConvert(string contentType) =>
            String.Equals(contentType, Name, StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<byte[]>>([data]);
    }

    [Theory]
    [InlineData("application/vnd.zebra-zpl", PrinterFormatKind.RawLanguage)]
    [InlineData("APPLICATION/VND.ELTRON-EPL", PrinterFormatKind.RawLanguage)]
    [InlineData("application/pdf", PrinterFormatKind.Document)]
    [InlineData("image/png", PrinterFormatKind.Image)]
    [InlineData("text/plain", PrinterFormatKind.Opaque)]
    [InlineData("application/octet-stream", PrinterFormatKind.Opaque)]
    public void KindOf_ReadsTheBuiltInFormats(string contentType, PrinterFormatKind expected)
    {
        Assert.Equal(expected, PrintFormatPolicy.Default.KindOf(contentType));
    }

    [Fact]
    public void KindOf_AnUnknownFormatIsOpaqueAndStillPrints()
    {
        // Opaque is the kind that travels unchanged, so an unregistered type is not refused.
        Assert.Equal(PrinterFormatKind.Opaque, PrintFormatPolicy.Default.KindOf("image/tiff"));
        Assert.False(PrintFormatPolicy.Default.IsRawLanguage("image/tiff"));
    }

    [Fact]
    public void KindOf_ReadsAFormatTheApplicationAdded()
    {
        PrintFormatPolicy policy = new([new PrinterFormat("application/vnd.star-line", PrinterFormatKind.RawLanguage, "STAR")], null);

        Assert.Equal(PrinterFormatKind.RawLanguage, policy.KindOf("application/vnd.star-line"));
        Assert.True(policy.IsRawLanguage("APPLICATION/VND.STAR-LINE"));
        Assert.Equal("STAR", policy.CommandSetFor("application/vnd.star-line"));
    }

    [Fact]
    public void Constructor_AFormatOfTheApplicationReplacesTheBuiltInOne()
    {
        PrintFormatPolicy policy = new([new PrinterFormat(PrinterContentTypes.Pdf, PrinterFormatKind.Opaque)], null);

        Assert.Equal(PrinterFormatKind.Opaque, policy.KindOf(PrinterContentTypes.Pdf));
        Assert.Equal(PrinterFormatKind.Document, PrintFormatPolicy.Default.KindOf(PrinterContentTypes.Pdf));
    }

    [Theory]
    [InlineData("application/vnd.zebra-zpl", "ZPL")]
    [InlineData("application/pdf", "PDF")]
    [InlineData("image/jpeg", "JPEG")]
    [InlineData("application/vnd.zebra-cpcl", "application/vnd.zebra-cpcl")]
    [InlineData("image/tiff", "image/tiff")]
    public void CommandSetFor_FallsBackToTheMediaType(string contentType, string expected)
    {
        Assert.Equal(expected, PrintFormatPolicy.Default.CommandSetFor(contentType));
    }

    [Fact]
    public void ConverterFor_AnswersTheConverterThatReadsTheFormat()
    {
        StubConverter tiff = new("image/tiff");
        PrintFormatPolicy policy = new(null, [tiff]);

        Assert.Same(tiff, policy.ConverterFor("image/tiff"));
        Assert.Null(policy.ConverterFor("image/heic"));
    }

    [Fact]
    public void ConverterFor_AConverterOfTheManagerWinsOverAProcessConverter()
    {
        const string ContentType = "application/x-format-policy-test";
        StubConverter process = new(ContentType);
        StubConverter own = new(ContentType);
        PrintFormatPolicy.AddDefaultConverter(process);

        // Added twice on purpose: the process list must not grow a duplicate.
        PrintFormatPolicy.AddDefaultConverter(process);

        Assert.Same(process, PrintFormatPolicy.Default.ConverterFor(ContentType));
        Assert.Same(own, new PrintFormatPolicy(null, [own]).ConverterFor(ContentType));
    }
}
