using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;
using IppScaling = SharpIpp.Protocol.Models.PrintScaling;
using PrintScaling = AdaptArch.Devices.Printing.PrintScaling;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppScalingMapperTests
{
    [Theory]
    [InlineData(PrintScaling.Auto, "auto")]
    [InlineData(PrintScaling.AutoFit, "auto-fit")]
    [InlineData(PrintScaling.Fill, "fill")]
    [InlineData(PrintScaling.Fit, "fit")]
    [InlineData(PrintScaling.None, "none")]
    public void Map_WritesTheKeyword(PrintScaling scaling, string expected) =>
        Assert.Equal(expected, IppScalingMapper.Map(scaling)?.Value);

    [Fact]
    public void Map_LeavesAnUnsetScalingUnset() =>
        Assert.Null(IppScalingMapper.Map((PrintScaling?)null));

    [Theory]
    [InlineData("auto", PrintScaling.Auto)]
    [InlineData("auto-fit", PrintScaling.AutoFit)]
    [InlineData("fill", PrintScaling.Fill)]
    [InlineData("fit", PrintScaling.Fit)]
    [InlineData("none", PrintScaling.None)]
    [InlineData("NONE", PrintScaling.None)]
    public void Map_ReadsTheKeyword(string keyword, PrintScaling expected) =>
        Assert.Equal(expected, IppScalingMapper.Map(new IppScaling(keyword, true)));

    [Fact]
    public void Map_IgnoresAKeywordTheLibraryDoesNotModel() =>
        Assert.Null(IppScalingMapper.Map(new IppScaling("shrink-to-fit", true)));
}
