using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterEndpointTests
{
    [Fact]
    public void NetworkEndpoint_DefaultsToRawPort()
    {
        var endpoint = NetworkPrinterEndpoint.Raw("printer.local");

        Assert.Equal(PrinterScheme.Raw, endpoint.Scheme);
        Assert.Equal("printer.local", endpoint.Host);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, endpoint.Port);
        Assert.Equal(9100, endpoint.Port);
    }

    [Fact]
    public void NetworkEndpoint_Equality_IsCaseInsensitiveOnHost()
    {
        Assert.Equal(NetworkPrinterEndpoint.Raw("HOST"), NetworkPrinterEndpoint.Raw("host"));
        Assert.NotEqual(NetworkPrinterEndpoint.Raw("host"), NetworkPrinterEndpoint.Raw("host", 9101));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void NetworkEndpoint_InvalidPort_Throws(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkPrinterEndpoint.Raw("host", port));
    }

    [Fact]
    public void SpoolerEndpoint_Equality_IgnoresCaseOfName()
    {
        // Windows and CUPS compare queue names without regard to case.
        Assert.Equal(new SpoolerPrinterEndpoint("Q"), new SpoolerPrinterEndpoint("Q"));
        Assert.Equal(new SpoolerPrinterEndpoint("Q"), new SpoolerPrinterEndpoint("q"));
        Assert.Equal(new SpoolerPrinterEndpoint("Q").GetHashCode(), new SpoolerPrinterEndpoint("q").GetHashCode());
        Assert.NotEqual(new SpoolerPrinterEndpoint("Q"), new SpoolerPrinterEndpoint("R"));
    }

    [Fact]
    public void SpoolerEndpoint_BlankName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SpoolerPrinterEndpoint("  "));
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("x?y")]
    [InlineData("x#y")]
    [InlineData("x\ty")]
    public void SpoolerEndpoint_NameWithAPathCharacter_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => new SpoolerPrinterEndpoint(name));

    [Fact]
    public void SpoolerEndpoint_NameLongerThanTheCupsLimit_Throws() =>
        Assert.Throws<ArgumentException>(() => new SpoolerPrinterEndpoint(new string('a', 128)));

    [Theory]
    [InlineData("Lobby Printer")]
    [InlineData("HP LaserJet (Copy 1)")]
    [InlineData("\\\\server\\queue")]
    public void SpoolerEndpoint_AcceptsTheNamesTheSpoolersUse(string name) =>
        Assert.Equal(name, new SpoolerPrinterEndpoint(name).Name);
}
