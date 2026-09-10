using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol.Models;
using Xunit;
using IppPrintQuality = SharpIpp.Protocol.Models.PrintQuality;
using IppRange = SharpIpp.Protocol.Models.Range;
using PrintColorMode = AdaptArch.Devices.Printing.PrintColorMode;
using PrintQuality = AdaptArch.Devices.Printing.PrintQuality;

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
            Scaling = AdaptArch.Devices.Printing.PrintScaling.Fit,
            MediaSize = "iso_a4_210x297mm",
            MediaSource = "tray-1",
            OutputBin = "face-down",
            ResolutionDpi = 600,
            Quality = PrintQuality.High,
            NumberUp = 2,
            PageRanges = [new PageRange(1, 3), new PageRange(7, 7)],
        };

        var template = IppJobTemplateMapper.Map(options);

        Assert.Equal(3, template.Copies);
        Assert.Equal("two-sided-long-edge", template.Sides?.Value);
        Assert.Equal("monochrome", template.PrintColorMode?.Value);
        Assert.Equal(Orientation.Landscape, template.OrientationRequested);
        Assert.Equal("fit", template.PrintScaling?.Value);
        Assert.Equal("iso_a4_210x297mm", template.Media?.Value);
        Assert.Equal("tray-1", template.MediaSource?.Value);
        Assert.Equal("face-down", template.OutputBin?.Value);
        Assert.Equal(600, template.PrinterResolution?.Width);
        Assert.Equal(ResolutionUnit.DotsPerInch, template.PrinterResolution?.Units);
        Assert.Equal(IppPrintQuality.High, template.PrintQuality);
        Assert.Equal(2, template.NumberUp);
        Assert.Equal([new IppRange(1, 3), new IppRange(7, 7)], template.PageRanges);
    }

    // RFC 8011 forbids "media" and "media-col" in one request, so a job that names a
    // media type carries the size inside "media-col" as well.
    [Fact]
    public void Map_MovesTheMediaSizeIntoMediaColWhenAMediaTypeIsSet()
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions
        {
            MediaSize = "iso_a4_210x297mm",
            MediaType = "labels",
        });

        Assert.Null(template.Media);
        Assert.Equal("labels", template.MediaCol?.MediaType?.Value);
        Assert.Equal("iso_a4_210x297mm", template.MediaCol?.MediaSizeName?.Value);
    }

    [Fact]
    public void Map_SendsTheMediaTypeAloneWhenNoSizeIsSet()
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { MediaType = "labels" });

        Assert.Null(template.Media);
        Assert.Equal("labels", template.MediaCol?.MediaType?.Value);
        Assert.Null(template.MediaCol?.MediaSizeName);
    }

    [Fact]
    public void Map_LeavesAnEmptyPageRangeListUnset()
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { PageRanges = [] });

        Assert.Null(template.PageRanges);
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
        Assert.Null(template.MediaCol);
        Assert.Null(template.OutputBin);
        Assert.Null(template.PrinterResolution);
        Assert.Null(template.PrintQuality);
        Assert.Null(template.NumberUp);
        Assert.Null(template.PageRanges);
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

    [Theory]
    [InlineData(PrintQuality.Draft, IppPrintQuality.Draft)]
    [InlineData(PrintQuality.Normal, IppPrintQuality.Normal)]
    [InlineData(PrintQuality.High, IppPrintQuality.High)]
    public void Map_TranslatesEveryQuality(PrintQuality quality, IppPrintQuality expected)
    {
        var template = IppJobTemplateMapper.Map(new PrintOptions { Quality = quality });

        Assert.Equal(expected, template.PrintQuality);
    }

    [Theory]
    [InlineData(PrintOrientation.Portrait, Orientation.Portrait)]
    [InlineData(PrintOrientation.Landscape, Orientation.Landscape)]
    [InlineData(PrintOrientation.ReverseLandscape, Orientation.ReverseLandscape)]
    [InlineData(PrintOrientation.ReversePortrait, Orientation.ReversePortrait)]
    public void Map_CarriesEveryRotation(PrintOrientation orientation, Orientation expected) =>
        Assert.Equal(expected, IppJobTemplateMapper.Map(new PrintOptions { Orientation = orientation }).OrientationRequested);
}
