using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterEndpointTests
{
    [Fact]
    public void NetworkEndpoint_DefaultsToRawPort()
    {
        NetworkPrinterEndpoint endpoint = new("printer.local");

        Assert.Equal(PrinterIdKind.Network, endpoint.Kind);
        Assert.Equal("printer.local", endpoint.Host);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, endpoint.Port);
        Assert.Equal(9100, endpoint.Port);
    }

    [Fact]
    public void NetworkEndpoint_Equality_IsCaseInsensitiveOnHost()
    {
        Assert.Equal(new NetworkPrinterEndpoint("HOST", 9100), new NetworkPrinterEndpoint("host", 9100));
        Assert.NotEqual(new NetworkPrinterEndpoint("host", 9100), new NetworkPrinterEndpoint("host", 9101));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void NetworkEndpoint_InvalidPort_Throws(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkPrinterEndpoint("host", port));
    }

    [Fact]
    public void UsbEndpoint_StoresDescriptors()
    {
        UsbPrinterEndpoint endpoint = new(0x0A5F, 0x0027, "SN123");

        Assert.Equal(PrinterIdKind.Usb, endpoint.Kind);
        Assert.Equal(0x0A5F, endpoint.VendorId);
        Assert.Equal(0x0027, endpoint.ProductId);
        Assert.Equal("SN123", endpoint.SerialNumber);
    }

    [Fact]
    public void UsbEndpoint_Equality_RequiresSameDescriptors()
    {
        Assert.Equal(new UsbPrinterEndpoint(1, 2, "S"), new UsbPrinterEndpoint(1, 2, "S"));
        Assert.NotEqual(new UsbPrinterEndpoint(1, 2, "S"), new UsbPrinterEndpoint(1, 2, null));
        Assert.NotEqual(new UsbPrinterEndpoint(1, 2), new UsbPrinterEndpoint(1, 3));
    }

    [Fact]
    public void SpoolerEndpoint_Equality_IsOrdinalOnName()
    {
        Assert.Equal(new SpoolerPrinterEndpoint("Q"), new SpoolerPrinterEndpoint("Q"));
        Assert.NotEqual(new SpoolerPrinterEndpoint("Q"), new SpoolerPrinterEndpoint("q"));
    }

    [Fact]
    public void SpoolerEndpoint_BlankName_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SpoolerPrinterEndpoint("  "));
    }
}
