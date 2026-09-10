using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class Ieee1284DeviceIdTests
{
    [Fact]
    public void TryParse_ReadsEveryKnownKey()
    {
        Assert.True(Ieee1284DeviceId.TryParse("MFG:EPSON;MDL:L6270;CMD:ESCPL2,BDC,D4;SN:X4TY012345;", out var id));
        Assert.Equal("EPSON", id.Manufacturer);
        Assert.Equal("L6270", id.Model);
        Assert.Equal("X4TY012345", id.SerialNumber);
        Assert.Equal(["ESCPL2", "BDC", "D4"], id.CommandSets);
    }

    [Fact]
    public void TryParse_KeepsAValueThatHoldsAColon()
    {
        Assert.True(Ieee1284DeviceId.TryParse("MFG:Zebra;CMD:ZPL:2,XML;", out var id));
        Assert.Equal(["ZPL:2", "XML"], id.CommandSets);
    }

    [Fact]
    public void TryParse_AcceptsALowercaseKeyAndAMissingFinalSemicolon()
    {
        Assert.True(Ieee1284DeviceId.TryParse("mfg:HP;mdl:LaserJet 400", out var id));
        Assert.Equal("HP", id.Manufacturer);
        Assert.Equal("LaserJet 400", id.Model);
    }

    [Fact]
    public void TryParse_TrimsSpaceAroundAPair()
    {
        Assert.True(Ieee1284DeviceId.TryParse(" MFG : Canon ; MDL : TR8500 ;", out var id));
        Assert.Equal("Canon", id.Manufacturer);
        Assert.Equal("TR8500", id.Model);
    }

    [Fact]
    public void TryParse_AcceptsTheLongKeyNames()
    {
        Assert.True(Ieee1284DeviceId.TryParse("MANUFACTURER:Brother;MODEL:QL-800;SERIALNUMBER:B1234;", out var id));
        Assert.Equal("Brother", id.Manufacturer);
        Assert.Equal("QL-800", id.Model);
        Assert.Equal("B1234", id.SerialNumber);
    }

    [Fact]
    public void TryParse_KeepsTheFirstValueOfARepeatedKey()
    {
        Assert.True(Ieee1284DeviceId.TryParse("MFG:First;MFG:Second;", out var id));
        Assert.Equal("First", id.Manufacturer);
    }

    [Fact]
    public void TryParse_ReportsNoSerialWhenTheDeviceGivesNone()
    {
        Assert.True(Ieee1284DeviceId.TryParse("MFG:EPSON;MDL:L6270;", out var id));
        Assert.Null(id.SerialNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-colon-here")]
    [InlineData(";;;")]
    public void TryParse_RefusesAValueWithNoPair(string value) =>
        Assert.False(Ieee1284DeviceId.TryParse(value, out _));
}
