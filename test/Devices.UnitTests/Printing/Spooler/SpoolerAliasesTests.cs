using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class SpoolerAliasesTests
{
    [Fact]
    public void FromDeviceUri_LinksAQueueToItsHost() =>
        Assert.Equal(
            [PrinterDeviceKey.ForHost("192.168.1.5")],
            SpoolerAliases.FromDeviceUri("socket://192.168.1.5:9100"));

    [Fact]
    public void FromDeviceUri_LinksAQueueToAUsbSerial() =>
        Assert.Equal(
            [PrinterDeviceKey.ForDeviceIdentity("X4TY012345")],
            SpoolerAliases.FromDeviceUri("usb://Zebra/ZTC%20ZD421?serial=X4TY012345"));

    [Fact]
    public void FromDeviceUri_LinksAQueueToAUuid() =>
        Assert.Equal(
            [PrinterDeviceKey.ForDeviceIdentity("e3248000-80ce-11db-8000-3c2af4a0d21d")],
            SpoolerAliases.FromDeviceUri("dnssd://Printer._ipp._tcp.local/?uuid=E3248000-80CE-11DB-8000-3C2AF4A0D21D"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("file:/dev/usb/lp0")]
    [InlineData("ipp://localhost:631/printers/x")]
    public void FromDeviceUri_GivesNothingWhenTheUriNamesNoDevice(string uri) =>
        Assert.Empty(SpoolerAliases.FromDeviceUri(uri));

    [Theory]
    [InlineData("IP_192.168.1.5", "192.168.1.5")]
    [InlineData("192.168.1.5", "192.168.1.5")]
    [InlineData("IP_192.168.1.5_1", "192.168.1.5")]
    [InlineData("IP_printer.corp.local", "printer.corp.local")]
    [InlineData("IP_192.168.1.5, IP_192.168.1.6", "192.168.1.5")]
    public void FromPortName_ReadsThePortsThatNameAnAddress(string portName, string expected) =>
        Assert.Equal([PrinterDeviceKey.ForHost(expected)], SpoolerAliases.FromPortName(portName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("USB001")]
    [InlineData("DOT4_001")]
    [InlineData("LPT1:")]
    [InlineData("COM1:")]
    [InlineData("FILE:")]
    [InlineData("PORTPROMPT:")]
    [InlineData("nul:")]
    [InlineData("WSD-1a2b3c4d-0000-0000-0000-000000000000.0021")]
    [InlineData("\\\\server\\queue")]
    [InlineData("IP_localhost")]
    [InlineData("IP_127.0.0.1")]
    public void FromPortName_GivesNothingWhenThePortNamesNoDevice(string portName) =>
        Assert.Empty(SpoolerAliases.FromPortName(portName));
}
