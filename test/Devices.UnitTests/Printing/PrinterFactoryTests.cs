using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterFactoryTests
{
    private static DiscoveredPrinter Found(PrinterEndpoint endpoint) =>
        new(PrinterId.FromNetwork("printer.local"), endpoint, new PrinterInfo(PrinterId.FromNetwork("printer.local"), "Lobby"));

    [Fact]
    public void Open_MakesAnIppPrinterForPort631()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(new NetworkPrinterEndpoint("printer.local", 631)));

        _ = Assert.IsType<IppPrinter>(printer);
    }

    [Fact]
    public void Open_MakesARawPrinterForPort9100()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(new NetworkPrinterEndpoint("printer.local", 9100)));

        _ = Assert.IsType<RawPrinter>(printer);
    }

    [Fact]
    public void Open_ThrowsForAUsbEndpoint()
    {
        PrinterFactory factory = new();
        var discovered = new DiscoveredPrinter(
            PrinterId.FromUsb("usb-1"),
            new UsbPrinterEndpoint(0x04B8, 0x0202),
            new PrinterInfo(PrinterId.FromUsb("usb-1"), "Label printer"));

        var error = Assert.Throws<NotSupportedException>(() => factory.Open(discovered));

        Assert.Contains("USB", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_MakesASpoolerPrinterForASpoolerEndpoint()
    {
        PrinterFactory factory = new();
        var discovered = new DiscoveredPrinter(
            PrinterId.FromSpooler("Lobby"),
            new SpoolerPrinterEndpoint("Lobby"),
            new PrinterInfo(PrinterId.FromSpooler("Lobby"), "Lobby"));

        var printer = factory.Open(discovered);

        _ = Assert.IsType<SpoolerPrinter>(printer);
    }

    [Fact]
    public async Task OpenAsync_ThrowsForAUsbIdentifier()
    {
        PrinterFactory factory = new();

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            factory.OpenAsync(PrinterId.FromUsb("usb-1"), TestContext.Current.CancellationToken));

        Assert.Contains("USB", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAsync_MakesASpoolerPrinterForASpoolerIdentifier()
    {
        PrinterFactory factory = new();

        var printer = await factory.OpenAsync(PrinterId.FromSpooler("Lobby"), TestContext.Current.CancellationToken);

        _ = Assert.IsType<SpoolerPrinter>(printer);
    }

    [Fact]
    public async Task OpenAsync_FallsBackToARawPrinterWhenIppDoesNotAnswer()
    {
        // 127.0.0.2 is a loopback address that nothing binds to in this test environment,
        // so the connection to port 631 is refused at once: no DNS lookup, no real network.
        PrinterFactory factory = new();

        var printer = await factory.OpenAsync(PrinterId.FromNetwork("127.0.0.2"), TestContext.Current.CancellationToken);

        var raw = Assert.IsType<RawPrinter>(printer);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(raw.Endpoint);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, endpoint.Port);
    }
}
