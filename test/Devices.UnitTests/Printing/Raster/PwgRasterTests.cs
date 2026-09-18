using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

public class PwgRasterTests
{
    [Theory]
    [InlineData("sgray_8", PwgRasterColorSpace.Grayscale8)]
    [InlineData("SGRAY_8", PwgRasterColorSpace.Grayscale8)]
    [InlineData("sgray_16", PwgRasterColorSpace.Grayscale8)]
    [InlineData("srgb_8", PwgRasterColorSpace.Srgb8)]
    [InlineData("adobe-rgb_8", PwgRasterColorSpace.Srgb8)]
    [InlineData(null, PwgRasterColorSpace.Srgb8)]
    [InlineData("", PwgRasterColorSpace.Srgb8)]
    public void ColorSpaceFor_ReadsTheKeyword(string type, PwgRasterColorSpace expected) =>
        Assert.Equal(expected, PwgRaster.ColorSpaceFor(type));

    [Theory]
    [InlineData("normal", PwgRasterSheetBack.Normal)]
    [InlineData("flipped", PwgRasterSheetBack.Flipped)]
    [InlineData("Flipped", PwgRasterSheetBack.Flipped)]
    [InlineData("rotated", PwgRasterSheetBack.Rotated)]
    [InlineData("manual-tumble", PwgRasterSheetBack.ManualTumble)]
    [InlineData(null, PwgRasterSheetBack.Normal)]
    [InlineData("something-new", PwgRasterSheetBack.Normal)]
    public void SheetBackFor_ReadsTheKeyword(string sheetBack, PwgRasterSheetBack expected) =>
        Assert.Equal(expected, PwgRaster.SheetBackFor(sheetBack));
}
