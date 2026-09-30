using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class UrfKeywordsTests
{
    private static readonly string[] AirPrint = ["V1.4", "CP1", "W8", "SRGB24", "RS300-600", "DM3", "IS1"];

    [Fact]
    public void Resolutions_ReadsEveryValueOfTheRsKeyword() =>
        Assert.Equal([300, 600], UrfKeywords.Resolutions(AirPrint));

    [Fact]
    public void Resolutions_AnswersNothingWithoutAnRsKeyword() =>
        Assert.Empty(UrfKeywords.Resolutions(["W8"]));

    [Fact]
    public void RasterTypes_NamesTheColourSpacesAsPwgDoes() =>
        Assert.Equal(["srgb_8", "sgray_8"], UrfKeywords.RasterTypes(AirPrint));

    [Theory]
    [InlineData("DM1", "normal")]
    [InlineData("DM2", "flipped")]
    [InlineData("DM3", "rotated")]
    [InlineData("DM4", "manual-tumble")]
    public void SheetBack_ReadsTheDmKeywordAsCupsDoes(string keyword, string expected) =>
        Assert.Equal(expected, UrfKeywords.SheetBack(["W8", keyword]));

    [Fact]
    public void SheetBack_AnswersNothingWithoutADmKeyword() =>
        Assert.Null(UrfKeywords.SheetBack(["W8"]));
}
