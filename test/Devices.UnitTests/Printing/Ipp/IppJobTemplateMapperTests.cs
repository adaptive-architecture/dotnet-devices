using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol.Models;
using Xunit;
using PrintColorMode = AdaptArch.Devices.Printing.PrintColorMode;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppJobTemplateMapperTests
{
    [Fact]
    public void Map_TranslatesEveryOption()
    {
        PrintOptions options = new()
        {
            Copies = 3,
            Duplex = DuplexMode.LongEdge,
            ColorMode = PrintColorMode.Monochrome,
            Orientation = PrintOrientation.Landscape,
            MediaSize = "iso_a4_210x297mm",
            MediaSource = "tray-1",
            ResolutionDpi = 600,
        };

        var template = IppJobTemplateMapper.Map(options);

        Assert.Equal(3, template.Copies);
        Assert.Equal("two-sided-long-edge", template.Sides?.Value);
        Assert.Equal("monochrome", template.PrintColorMode?.Value);
        Assert.Equal(Orientation.Landscape, template.OrientationRequested);
        Assert.Equal("iso_a4_210x297mm", template.Media?.Value);
        Assert.Equal("tray-1", template.MediaSource?.Value);
        Assert.Equal(600, template.PrinterResolution?.Width);
        Assert.Equal(ResolutionUnit.DotsPerInch, template.PrinterResolution?.Units);
    }

    [Fact]
    public void Map_LeavesEveryUnsetOptionUnset()
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions());

        Assert.Null(template.Copies);
        Assert.Null(template.Sides);
        Assert.Null(template.PrintColorMode);
        Assert.Null(template.OrientationRequested);
        Assert.Null(template.Media);
        Assert.Null(template.MediaSource);
        Assert.Null(template.PrinterResolution);
    }

    [Fact]
    public void Map_ReturnsAnEmptyTemplateForNoOptions()
    {
        var template = IppJobTemplateMapper.Map(null);

        Assert.Null(template.Copies);
    }

    [Theory]
    [InlineData(DuplexMode.Simplex, "one-sided")]
    [InlineData(DuplexMode.LongEdge, "two-sided-long-edge")]
    [InlineData(DuplexMode.ShortEdge, "two-sided-short-edge")]
    public void Map_TranslatesEveryDuplexMode(DuplexMode duplex, string expected)
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { Duplex = duplex });

        Assert.Equal(expected, template.Sides?.Value);
    }

    [Theory]
    [InlineData(PrintColorMode.Monochrome, "monochrome")]
    [InlineData(PrintColorMode.Color, "color")]
    public void Map_TranslatesEveryColorMode(PrintColorMode mode, string expected)
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { ColorMode = mode });

        Assert.Equal(expected, template.PrintColorMode?.Value);
    }

    [Theory]
    [InlineData(PrintOrientation.Portrait, Orientation.Portrait)]
    [InlineData(PrintOrientation.Landscape, Orientation.Landscape)]
    public void Map_TranslatesEveryOrientation(PrintOrientation orientation, Orientation expected)
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { Orientation = orientation });

        Assert.Equal(expected, template.OrientationRequested);
    }
}
