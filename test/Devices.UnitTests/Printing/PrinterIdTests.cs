using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterIdTests
{
    [Fact]
    public void FromSpooler_CreatesSpoolerId()
    {
        var id = PrinterId.FromSpooler("Office-Laser");

        Assert.Equal(PrinterIdKind.Spooler, id.Kind);
        Assert.Equal("Office-Laser", id.Value);
    }

    [Fact]
    public void FromNetwork_CreatesNetworkId()
    {
        var id = PrinterId.FromNetwork("192.168.1.50");

        Assert.Equal(PrinterIdKind.Network, id.Kind);
        Assert.Equal("192.168.1.50", id.Value);
    }

    [Fact]
    public void FromUsb_CreatesUsbId()
    {
        var id = PrinterId.FromUsb("Zebra-1234");

        Assert.Equal(PrinterIdKind.Usb, id.Kind);
        Assert.Equal("Zebra-1234", id.Value);
    }

    [Fact]
    public void Equality_SameKindAndValue_AreEqual()
    {
        Assert.Equal(PrinterId.FromSpooler("A"), PrinterId.FromSpooler("A"));
        Assert.True(PrinterId.FromSpooler("A") == PrinterId.FromSpooler("A"));
        Assert.False(PrinterId.FromSpooler("A") != PrinterId.FromSpooler("A"));
    }

    [Fact]
    public void Equality_DifferentKindOrValue_AreNotEqual()
    {
        Assert.NotEqual(PrinterId.FromSpooler("A"), PrinterId.FromNetwork("A"));
        Assert.NotEqual(PrinterId.FromSpooler("A"), PrinterId.FromSpooler("B"));
        Assert.True(PrinterId.FromSpooler("A") != PrinterId.FromNetwork("A"));
    }

    [Fact]
    public void ToString_IncludesKindAndValue()
    {
        Assert.Equal("Spooler:Office-Laser", PrinterId.FromSpooler("Office-Laser").ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankValue_Throws(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PrinterId(PrinterIdKind.Spooler, value));
    }
}
