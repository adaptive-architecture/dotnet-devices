using AdaptArch.Devices.Printing.Raster;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Raster;

public class PwgRasterTests
{
    [Theory]
    [InlineData("sgray_8", RasterColorSpace.Grayscale8)]
    [InlineData("SGRAY_8", RasterColorSpace.Grayscale8)]
    [InlineData("sgray_16", RasterColorSpace.Grayscale8)]
    [InlineData("srgb_8", RasterColorSpace.Srgb8)]
    [InlineData("adobe-rgb_8", RasterColorSpace.Srgb8)]
    [InlineData(null, RasterColorSpace.Srgb8)]
    [InlineData("", RasterColorSpace.Srgb8)]
    public void ColorSpaceFor_ReadsTheKeyword(string type, RasterColorSpace expected) =>
        Assert.Equal(expected, PwgRaster.ColorSpaceFor(type));

    [Theory]
    [InlineData("normal", RasterSheetBack.Normal)]
    [InlineData("flipped", RasterSheetBack.Flipped)]
    [InlineData("Flipped", RasterSheetBack.Flipped)]
    [InlineData("rotated", RasterSheetBack.Rotated)]
    [InlineData("manual-tumble", RasterSheetBack.ManualTumble)]
    [InlineData(null, RasterSheetBack.Normal)]
    [InlineData("something-new", RasterSheetBack.Normal)]
    public void SheetBackFor_ReadsTheKeyword(string sheetBack, RasterSheetBack expected) =>
        Assert.Equal(expected, PwgRaster.SheetBackFor(sheetBack));
}
