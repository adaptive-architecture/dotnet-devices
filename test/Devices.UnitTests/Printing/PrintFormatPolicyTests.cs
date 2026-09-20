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

    [Fact]
    public void Name_DefaultsToTheTypeName()
    {
        // A converter nobody chooses between needs to do nothing, and still has a name that
        // a message can print. The default lives on the interface, so it is reachable
        // through it and not through the concrete type, which is how the library sees one.
        IPrintPayloadConverter converter = new UnnamedConverter();

        Assert.Equal(nameof(UnnamedConverter), converter.Name);
    }

    [Fact]
    public void ConvertersFor_ListsEveryConverterOfTheFormatBestFirst()
    {
        const string ContentType = "application/x-two-engines";
        NamedConverter first = new(ContentType, "First");
        NamedConverter second = new(ContentType, "Second");
        NamedConverter other = new("image/tiff", "Tiff");
        PrintFormatPolicy policy = new(null, [first, second, other]);

        // Order carries the preference, so the head of the list is what ConverterFor answers
        // and an application needs no second concept to show which is the default.
        Assert.Equal<IPrintPayloadConverter[]>([first, second], [.. policy.ConvertersFor(ContentType)]);
        Assert.Same(first, policy.ConverterFor(ContentType));
        Assert.Empty(policy.ConvertersFor("image/heic"));
    }

    [Fact]
    public void ConvertersFor_PutsTheManagerConvertersAheadOfTheProcessOnes()
    {
        const string ContentType = "application/x-two-engines-scoped";
        NamedConverter process = new(ContentType, "Process");
        NamedConverter own = new(ContentType, "Own");
        PrintFormatPolicy.AddDefaultConverter(process);

        Assert.Equal<IPrintPayloadConverter[]>(
            [own, process],
            [.. new PrintFormatPolicy(null, [own]).ConvertersFor(ContentType)]);
    }

    [Theory]
    [InlineData("Second")]
    [InlineData("SECOND")]
    [InlineData("second")]
    public void ConverterFor_AName_TakesThatConverterAndNotThePreferredOne(string asked)
    {
        const string ContentType = "application/x-named-engine";
        NamedConverter first = new(ContentType, "First");
        NamedConverter second = new(ContentType, "Second");
        PrintFormatPolicy policy = new(null, [first, second]);

        // Case-insensitive, because the name reaches this from a JSON file or a form field.
        Assert.Same(second, policy.ConverterFor(ContentType, asked));
    }

    [Fact]
    public void ConverterFor_NoName_KeepsThePreferredConverter()
    {
        const string ContentType = "application/x-unnamed-ask";
        NamedConverter first = new(ContentType, "First");
        PrintFormatPolicy policy = new(null, [first, new NamedConverter(ContentType, "Second")]);

        Assert.Same(first, policy.ConverterFor(ContentType, null));
    }

    [Fact]
    public void ConverterFor_ANameNobodyCarries_AnswersNothingRatherThanTheWrongEngine()
    {
        const string ContentType = "application/x-missing-engine";
        PrintFormatPolicy policy = new(null, [new NamedConverter(ContentType, "First")]);

        // Answering the preferred converter here would render a job with an engine it did
        // not ask for. The caller turns this null into a failure that names what exists.
        Assert.Null(policy.ConverterFor(ContentType, "Second"));
    }

    private sealed class NamedConverter(string contentType, string name) : IPrintPayloadConverter
    {
        public string Name { get; } = name;

        public bool CanConvert(string type) =>
            String.Equals(type, contentType, StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<byte[]>>([data]);
    }

    private sealed class UnnamedConverter : IPrintPayloadConverter
    {
        public bool CanConvert(string contentType) => false;

        public Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<byte[]>>([data]);
    }
}
