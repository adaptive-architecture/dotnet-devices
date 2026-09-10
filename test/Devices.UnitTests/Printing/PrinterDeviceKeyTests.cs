using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterDeviceKeyTests
{
    [Fact]
    public void ForDeviceIdentity_LowercasesSoTheSourcesAgree()
    {
        var upper = PrinterDeviceKey.ForDeviceIdentity("E3B0C442-98FC-1C14-9AFB-4C8996FB9242");
        var lower = PrinterDeviceKey.ForDeviceIdentity("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        Assert.Equal(lower, upper);
        Assert.True(upper.IsDeviceIdentity);
    }

    [Fact]
    public void ForHost_ComparesWithoutRegardToCase() =>
        Assert.Equal(PrinterDeviceKey.ForHost("Printer.Local"), PrinterDeviceKey.ForHost("printer.local"));

    [Fact]
    public void ForQueue_ComparesWithoutRegardToCase() =>
        Assert.Equal(PrinterDeviceKey.ForQueue("EPSON_L6270"), PrinterDeviceKey.ForQueue("epson_l6270"));

    [Fact]
    public void AKeyOfOneKindNeverEqualsAKeyOfAnother() =>
        Assert.NotEqual(PrinterDeviceKey.ForHost("192.168.1.5"), PrinterDeviceKey.ForQueue("192.168.1.5"));

    [Theory]
    [InlineData("X4TY012345")]
    [InlineData("e3b0c442-98fc-1c14-9afb-4c8996fb9242")]
    [InlineData("ABC")]
    public void IsUsableIdentity_AcceptsSomethingThatNamesADevice(string value) =>
        Assert.True(PrinterDeviceKey.IsUsableIdentity(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("n/a")]
    [InlineData("N/A")]
    [InlineData("none")]
    [InlineData("unknown")]
    [InlineData("SN:")]
    [InlineData("serial")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0000")]
    [InlineData("-----")]
    public void IsUsableIdentity_RefusesAPlaceholder(string value) =>
        Assert.False(PrinterDeviceKey.IsUsableIdentity(value));

    [Fact]
    public void ForDeviceIdentity_ThrowsOnAPlaceholder() =>
        Assert.Throws<ArgumentException>(() => PrinterDeviceKey.ForDeviceIdentity("none"));

    [Fact]
    public void Rank_PrefersAUuidThenASerialThenAnAddress()
    {
        var uuid = PrinterDeviceKey.Rank(PrinterDeviceKey.ForDeviceIdentity("e3b0c442-98fc-1c14-9afb-4c8996fb9242"));
        var serial = PrinterDeviceKey.Rank(PrinterDeviceKey.ForDeviceIdentity("X4TY012345"));
        var host = PrinterDeviceKey.Rank(PrinterDeviceKey.ForHost("192.168.1.5"));
        var queue = PrinterDeviceKey.Rank(PrinterDeviceKey.ForQueue("EPSON"));
        Assert.True(uuid < serial);
        Assert.True(serial < host);
        Assert.True(host < queue);
    }
}
