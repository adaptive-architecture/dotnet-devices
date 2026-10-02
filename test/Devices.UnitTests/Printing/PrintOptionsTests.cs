using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-300)]
    [InlineData(PrintOptions.MaxResolutionDpi + 1)]
    public void ResolutionDpi_OutsideTheBand_IsRefused(int dpi) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintOptions { ResolutionDpi = dpi });

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    [InlineData(PrintOptions.MaxResolutionDpi)]
    public void ResolutionDpi_InsideTheBand_IsKept(int dpi) =>
        Assert.Equal(dpi, new PrintOptions { ResolutionDpi = dpi }.ResolutionDpi);
}
