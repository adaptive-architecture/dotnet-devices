using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

// No P/Invoke: the mapper only turns options into numbers, so it runs on any platform.
public class WindowsSpoolerDeviceModeMapperTests
{
    private static readonly IReadOnlyList<PrinterMedia> Media = [new PrinterMedia("A4", 9), new PrinterMedia("Label 4x6", 256)];
    private static readonly IReadOnlyList<PrinterMediaSource> Sources = [new PrinterMediaSource("Tray 2", 2), new PrinterMediaSource("Roll", null)];

    [Fact]
    public void Build_IsEmptyWithoutOptions()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(null, [], []);

        Assert.True(request.IsEmpty);
        Assert.Empty(request.Dropped);
    }

    [Fact]
    public void Build_IsEmptyWhenOnlyTheJobNameIsSet()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { JobName = "label" }, [], []);

        Assert.True(request.IsEmpty);
        Assert.Empty(request.Dropped);
    }

    [Fact]
    public void Build_MapsEveryFieldTheDeviceModeCarries()
    {
        PrintOptions options = new()
        {
            Orientation = PrintOrientation.Landscape,
            MediaSize = "Label 4x6",
            MediaSource = "Tray 2",
            ColorMode = PrintColorMode.Color,
            Duplex = DuplexMode.ShortEdge,
            ResolutionDpi = 300,
        };

        var request = WindowsSpoolerDeviceModeMapper.Build(options, Media, Sources);

        Assert.Equal((short)2, request.Orientation);
        Assert.Equal((short)256, request.PaperSize);
        Assert.Equal((short)2, request.DefaultSource);
        Assert.Equal((short)2, request.Color);
        Assert.Equal((short)3, request.Duplex);
        Assert.Equal((short)300, request.PrintQuality);
        Assert.Equal((short)300, request.YResolution);
        Assert.Empty(request.Dropped);
    }

    [Fact]
    public void Build_SetsOneFieldBitForEveryMappedField()
    {
        PrintOptions options = new()
        {
            Orientation = PrintOrientation.Portrait,
            MediaSize = "A4",
            MediaSource = "Tray 2",
            ColorMode = PrintColorMode.Monochrome,
            Duplex = DuplexMode.Simplex,
            ResolutionDpi = 600,
        };

        const uint expected = WindowsSpoolerCapabilityParser.DmOrientation
            | WindowsSpoolerCapabilityParser.DmPaperSize
            | WindowsSpoolerCapabilityParser.DmDefaultSource
            | WindowsSpoolerCapabilityParser.DmColor
            | WindowsSpoolerCapabilityParser.DmDuplex
            | WindowsSpoolerCapabilityParser.DmPrintQuality
            | WindowsSpoolerCapabilityParser.DmYResolution;

        Assert.Equal(expected, WindowsSpoolerDeviceModeMapper.Build(options, Media, Sources).Fields);
    }

    [Theory]
    [InlineData(PrintQuality.Draft, (short)-1)]
    [InlineData(PrintQuality.Normal, (short)-3)]
    [InlineData(PrintQuality.High, (short)-4)]
    public void Build_MapsTheQualityToADmResName(PrintQuality quality, short expected)
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { Quality = quality }, [], []);

        Assert.Equal(expected, request.PrintQuality);
        Assert.Null(request.YResolution);
        Assert.Equal(WindowsSpoolerCapabilityParser.DmPrintQuality, request.Fields);
    }

    [Fact]
    public void Build_KeepsTheResolutionAndDropsTheQualityWhenBothAreSet()
    {
        PrintOptions options = new() { ResolutionDpi = 203, Quality = PrintQuality.High };

        var request = WindowsSpoolerDeviceModeMapper.Build(options, [], []);

        Assert.Equal((short)203, request.PrintQuality);
        Assert.Equal([nameof(PrintOptions.Quality)], request.Dropped);
    }

    [Fact]
    public void Build_DropsAResolutionNoFieldCanHold()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { ResolutionDpi = 40000 }, [], []);

        Assert.True(request.IsEmpty);
        Assert.Equal([nameof(PrintOptions.ResolutionDpi)], request.Dropped);
    }

    [Fact]
    public void Build_DropsAMediaNameTheQueueDidNotReport()
    {
        PrintOptions options = new() { MediaSize = "Letter", MediaSource = "Roll" };

        var request = WindowsSpoolerDeviceModeMapper.Build(options, Media, Sources);

        Assert.True(request.IsEmpty);
        Assert.Equal([nameof(PrintOptions.MediaSource), nameof(PrintOptions.MediaSize)], request.Dropped);
    }

    [Fact]
    public void Build_CountsCopiesAsApplied()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { Copies = 3 }, [], []);

        Assert.True(request.IsEmpty);
        Assert.Empty(request.Dropped);
    }

    [Fact]
    public void Build_DropsTheOptionsWithNoDeviceModeField()
    {
        PrintOptions options = new()
        {
            MediaType = "labels",
            OutputBin = "face-down",
            PageRanges = [new PageRange(1, 2)],
            NumberUp = 2,
        };

        Assert.Equal(
            [
                nameof(PrintOptions.MediaType),
                nameof(PrintOptions.OutputBin),
                nameof(PrintOptions.PageRanges),
                nameof(PrintOptions.NumberUp),
            ],
            WindowsSpoolerDeviceModeMapper.Build(options, Media, Sources).Dropped);
    }

    // A driver-private paper number above 0x7FFF is written as the negative it was.
    [Fact]
    public void Build_CarriesAPaperNumberAboveTheSignedRange()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(
            new PrintOptions { MediaSize = "Custom" }, [new PrinterMedia("Custom", 0xF000)], []);

        Assert.Equal(unchecked((short)0xF000), request.PaperSize);
    }

    // dmScale is a percentage, so "do not scale" is the only mode a device mode can hold.
    [Fact]
    public void Build_MapsScalingNoneToOneHundredPercent()
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(
            new PrintOptions { Scaling = PrintScaling.None }, [], []);

        Assert.Equal((short)100, request.Scale);
        Assert.Equal(WindowsSpoolerCapabilityParser.DmScale, request.Fields & WindowsSpoolerCapabilityParser.DmScale);
        Assert.Empty(request.Dropped);
    }

    [Theory]
    [InlineData(PrintScaling.Auto)]
    [InlineData(PrintScaling.AutoFit)]
    [InlineData(PrintScaling.Fill)]
    [InlineData(PrintScaling.Fit)]
    public void Build_DropsAFitModeADeviceModeCannotHold(PrintScaling scaling)
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { Scaling = scaling }, [], []);

        Assert.Null(request.Scale);
        Assert.Contains(nameof(PrintOptions.Scaling), request.Dropped);
    }

    [Theory]
    [InlineData(PrintOrientation.ReverseLandscape)]
    [InlineData(PrintOrientation.ReversePortrait)]
    public void Build_DropsARotationADeviceModeCannotHold(PrintOrientation orientation)
    {
        var request = WindowsSpoolerDeviceModeMapper.Build(new PrintOptions { Orientation = orientation }, [], []);

        Assert.Null(request.Orientation);
        Assert.Contains(nameof(PrintOptions.Orientation), request.Dropped);
    }
}
