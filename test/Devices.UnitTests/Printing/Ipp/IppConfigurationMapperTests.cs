using System.Linq;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppConfigurationMapperTests
{
    private static readonly PrinterId Id = PrinterId.ForRaw("printer.local");

    // RFC 8010: width and height as 4-byte integers, then a 1-byte unit (3 = dpi, 4 = dpcm).
    private static byte[] Resolution(int width, int height, byte units) =>
    [
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        units,
    ];

    // RFC 8010 rangeOfInteger: the lower and the upper bound as 4-byte integers.
    private static byte[] Range(int lower, int upper) =>
    [
        (byte)(lower >> 24), (byte)(lower >> 16), (byte)(lower >> 8), (byte)lower,
        (byte)(upper >> 24), (byte)(upper >> 16), (byte)(upper >> 8), (byte)upper,
    ];

    [Fact]
    public async Task Map_ReadsResolutionsSidesColourAndMedia()
    {
        // 0x32 is the resolution tag; the value is width, height and the unit 3 (dpi).
        var body = IppMessages.Response(0x0000,
            (0x32, "printer-resolution-supported", Resolution(600, 600, 3)),
            (0x32, null, Resolution(300, 300, 3)),
            (0x44, "sides-supported", "one-sided"),
            (0x44, null, "two-sided-long-edge"),
            (0x22, "color-supported", (byte)1),
            (0x44, "media-supported", "iso_a4_210x297mm"),
            (0x44, null, "na_letter_8.5x11in"),
            (0x44, "media-default", "iso_a4_210x297mm"));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Equal(Id, configuration.PrinterId);
        Assert.True(configuration.SupportsDuplex);
        Assert.True(configuration.SupportsColor);
        Assert.Equal(["iso_a4_210x297mm", "na_letter_8.5x11in"], configuration.MediaSizes);
        Assert.Equal("iso_a4_210x297mm", configuration.DefaultMediaSize);
        Assert.Equal([600, 300], configuration.SupportedResolutionsDpi);
        Assert.Equal(["iso_a4_210x297mm", "na_letter_8.5x11in"], configuration.Media.Select(static entry => entry.Name));
        // IPP knows no Windows paper number, so every entry carries the name only.
        Assert.All(configuration.Media, static entry => Assert.Null(entry.WindowsPaperNumber));
    }

    [Fact]
    public async Task Map_ReadsTheTraysTheTypesAndTheBins()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "media-source-supported", "tray-1"),
            (0x44, null, "tray-2"),
            (0x44, null, "manual"),
            (0x44, "media-type-supported", "stationery"),
            (0x44, null, "labels"),
            (0x44, "output-bin-supported", "face-down"),
            (0x44, null, "face-up"));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Equal(["tray-1", "tray-2", "manual"], configuration.MediaSources.Select(static source => source.Name));
        Assert.All(configuration.MediaSources, static source => Assert.Null(source.WindowsBinNumber));
        Assert.Equal(["stationery", "labels"], configuration.MediaTypes);
        Assert.Equal(["face-down", "face-up"], configuration.OutputBins);
    }

    [Fact]
    public async Task Map_ReadsTheQualitiesThePagesPerSheetAndThePageRangeFlag()
    {
        // 0x23 is the enum tag, 0x21 the integer tag, 0x33 rangeOfInteger, 0x22 boolean.
        var body = IppMessages.Response(0x0000,
            (0x23, "print-quality-supported", 3),
            (0x23, null, 5),
            (0x21, "number-up-supported", 1),
            (0x33, null, Range(2, 4)),
            (0x22, "page-ranges-supported", (byte)1));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Equal([PrintQuality.Draft, PrintQuality.High], configuration.Qualities);
        // The range is expanded: the model holds the values a caller may ask for.
        Assert.Equal([1, 2, 3, 4], configuration.NumberUpValues);
        Assert.True(configuration.SupportsPageRanges);
    }

    [Fact]
    public async Task Map_ReadsTheDefaultOrientationAndResolution()
    {
        var body = IppMessages.Response(0x0000,
            (0x23, "orientation-requested-default", 4),
            (0x32, "printer-resolution-default", Resolution(300, 300, 3)));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Equal(PrintOrientation.Landscape, configuration.DefaultOrientation);
        Assert.Equal(300, configuration.DefaultResolutionDpi);
    }

    [Fact]
    public async Task Map_DropsADefaultResolutionThatIsNotInDotsPerInch()
    {
        // Unit 4 is dots per centimetre, which the model does not hold.
        var body = IppMessages.Response(0x0000,
            (0x32, "printer-resolution-default", Resolution(120, 120, 4)));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Null(configuration.DefaultResolutionDpi);
    }

    // SharpIppNext does not carry "media-source-default", so it comes from the raw response.
    [Fact]
    public async Task Map_ReadsTheDefaultTrayFromTheRawResponse()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "media-source-supported", "tray-1"),
            (0x44, null, "tray-2"),
            (0x44, "media-source-default", "tray-2"));
        (var attributes, var raw) = await IppMessages.DecodeWithRawAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, raw);

        Assert.Equal("tray-2", configuration.DefaultMediaSource);
    }

    [Fact]
    public async Task Map_ReportsNoDuplexWhenOnlyOneSidedIsSupported()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.False(configuration.SupportsDuplex);
        Assert.False(configuration.SupportsColor);
    }

    [Fact]
    public async Task Map_ReadsTheSupportedDocumentFormats()
    {
        // 0x49 is the mimeMediaType tag.
        var body = IppMessages.Response(0x0000,
            (0x49, "document-format-supported", "application/pdf"),
            (0x49, null, "application/vnd.cups-raw"),
            (0x49, null, "application/octet-stream"));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes, null);

        Assert.Equal(
            ["application/pdf", "application/vnd.cups-raw", "application/octet-stream"],
            configuration.SupportedDocumentFormats);
    }

    [Fact]
    public void Map_ReturnsAnEmptyConfigurationForNoAttributes()
    {
        var configuration = IppConfigurationMapper.Map(Id, null, null);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Null(configuration.SupportsDuplex);
        Assert.Null(configuration.SupportsColor);
        Assert.Null(configuration.DefaultMediaSize);
        Assert.Empty(configuration.SupportedDocumentFormats);
        Assert.Empty(configuration.Media);
        Assert.Empty(configuration.MediaSources);
        Assert.Empty(configuration.MediaTypes);
        Assert.Empty(configuration.OutputBins);
        Assert.Empty(configuration.Qualities);
        Assert.Empty(configuration.NumberUpValues);
        Assert.Null(configuration.SupportsPageRanges);
        Assert.Null(configuration.DefaultMediaSource);
        Assert.Null(configuration.DefaultOrientation);
        Assert.Null(configuration.DefaultResolutionDpi);
    }
}
