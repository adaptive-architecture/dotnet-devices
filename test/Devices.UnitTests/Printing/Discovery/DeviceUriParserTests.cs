using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

public class DeviceUriParserTests
{
    [Theory]
    [InlineData("ipp://192.168.1.5:631/ipp/print", "192.168.1.5")]
    [InlineData("ipps://printer.local/ipp/print", "printer.local")]
    [InlineData("socket://192.168.1.5:9100", "192.168.1.5")]
    [InlineData("http://192.168.1.5:631/printers/x", "192.168.1.5")]
    [InlineData("lpd://printer.local/queue", "printer.local")]
    public void TryParse_ReadsTheHostOfANetworkQueue(string uri, string expected)
    {
        Assert.True(DeviceUriParser.TryParse(uri, out var parsed));
        Assert.Equal(expected, parsed.Host);
    }

    [Fact]
    public void TryParse_ReadsANonDefaultPort()
    {
        Assert.True(DeviceUriParser.TryParse("socket://192.168.1.5:9101", out var parsed));
        Assert.Equal(9101, parsed.Port);
    }

    [Fact]
    public void TryParse_ReadsTheSerialAndTheModelOfAUsbQueue()
    {
        Assert.True(DeviceUriParser.TryParse("usb://Zebra/ZTC%20ZD421?serial=X4TY012345", out var parsed));
        Assert.Equal("X4TY012345", parsed.SerialNumber);
        Assert.Equal("Zebra", parsed.Manufacturer);
        Assert.Equal("ZTC ZD421", parsed.Model);
        Assert.Null(parsed.Host);
    }

    [Fact]
    public void TryParse_ReadsTheUuidOfAMulticastDnsQueue()
    {
        Assert.True(DeviceUriParser.TryParse("dnssd://Printer._ipp._tcp.local/?uuid=E3248000-80CE-11DB-8000-3C2AF4A0D21D", out var parsed));
        Assert.Equal("e3248000-80ce-11db-8000-3c2af4a0d21d", parsed.Uuid);
        Assert.Null(parsed.Host);
    }

    [Fact]
    public void TryParse_ReadsAQueryOfASchemeWithNoAuthority()
    {
        Assert.True(DeviceUriParser.TryParse("hp:/usb/HP_LaserJet?serial=VN12345", out var parsed));
        Assert.Equal("VN12345", parsed.SerialNumber);
    }

    [Theory]
    [InlineData("ipp://localhost:631/printers/x")]
    [InlineData("ipp://127.0.0.1:631/printers/x")]
    [InlineData("ipp://[::1]:631/printers/x")]
    [InlineData("socket://0.0.0.0:9100")]
    public void TryParse_RefusesALocalDaemonThatWouldFuseEveryQueue(string uri) =>
        Assert.False(DeviceUriParser.TryParse(uri, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("file:/dev/usb/lp0")]
    [InlineData("cups-brf:/")]
    // A CUPS driverless queue points at a class, and a discovered one at a service
    // instance. Neither authority is an address: reading one as a host would invent a
    // device from the queue name written a second way.
    [InlineData("implicitclass://EPSON_L6270_Series/")]
    [InlineData("dnssd://EPSON%20L6270%20Series._ipp._tcp.local/")]
    [InlineData("beh:/1/3/5/socket://printer")]
    public void TryParse_RefusesAUriThatNamesNoDevice(string uri) =>
        Assert.False(DeviceUriParser.TryParse(uri, out _));

    [Fact]
    public void TryParse_StillReadsAnIdentityFromTheQueryOfANonHostScheme()
    {
        Assert.True(DeviceUriParser.TryParse("dnssd://Printer._ipp._tcp.local/?uuid=e3248000-80ce-11db-8000-3c2af4a0d21d", out var parsed));
        Assert.Null(parsed.Host);
        Assert.Equal("e3248000-80ce-11db-8000-3c2af4a0d21d", parsed.Uuid);
    }

    [Theory]
    [InlineData("urn:uuid:E3248000-80CE-11DB-8000-3C2AF4A0D21D", "e3248000-80ce-11db-8000-3c2af4a0d21d")]
    [InlineData("{e3248000-80ce-11db-8000-3c2af4a0d21d}", "e3248000-80ce-11db-8000-3c2af4a0d21d")]
    [InlineData("e3248000-80ce-11db-8000-3c2af4a0d21d", "e3248000-80ce-11db-8000-3c2af4a0d21d")]
    public void NormalizeUuid_StripsThePrefixAndTheBraces(string value, string expected) =>
        Assert.Equal(expected, DeviceUriParser.NormalizeUuid(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    public void NormalizeUuid_ReturnsNullWhenTheValueIsNotOne(string value) =>
        Assert.Null(DeviceUriParser.NormalizeUuid(value));
}
