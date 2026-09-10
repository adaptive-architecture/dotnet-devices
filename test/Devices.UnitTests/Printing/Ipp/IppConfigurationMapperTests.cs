using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppConfigurationMapperTests
{
    private static readonly PrinterId Id = PrinterId.FromNetwork("printer.local");

    // RFC 8010: width and height as 4-byte integers, then a 1-byte unit (3 = dpi, 4 = dpcm).
    private static byte[] Resolution(int width, int height, byte units) =>
    [
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        units,
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

        var configuration = IppConfigurationMapper.Map(Id, attributes);

        Assert.Equal(Id, configuration.PrinterId);
        Assert.True(configuration.SupportsDuplex);
        Assert.True(configuration.SupportsColor);
        Assert.Equal(["iso_a4_210x297mm", "na_letter_8.5x11in"], configuration.MediaSizes);
        Assert.Equal("iso_a4_210x297mm", configuration.DefaultMediaSize);
        Assert.Equal([600, 300], configuration.SupportedResolutionsDpi);
    }

    [Fact]
    public async Task Map_ReportsNoDuplexWhenOnlyOneSidedIsSupported()
    {
        var body = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0));
        var attributes = await IppMessages.DecodePrinterAttributesAsync(body);

        var configuration = IppConfigurationMapper.Map(Id, attributes);

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

        var configuration = IppConfigurationMapper.Map(Id, attributes);

        Assert.Equal(
            ["application/pdf", "application/vnd.cups-raw", "application/octet-stream"],
            configuration.SupportedDocumentFormats);
    }

    [Fact]
    public void Map_ReturnsAnEmptyConfigurationForNoAttributes()
    {
        var configuration = IppConfigurationMapper.Map(Id, null);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Null(configuration.SupportsDuplex);
        Assert.Null(configuration.SupportsColor);
        Assert.Null(configuration.DefaultMediaSize);
        Assert.Empty(configuration.SupportedDocumentFormats);
    }
}
