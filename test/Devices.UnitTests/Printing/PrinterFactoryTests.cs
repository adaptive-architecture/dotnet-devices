using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterFactoryTests
{
    private static DiscoveredPrinter Found(PrinterEndpoint endpoint) =>
        new(PrinterId.ForRaw("printer.local"), endpoint, new PrinterInfo(PrinterId.ForRaw("printer.local"), "Lobby"));

    [Fact]
    public void Open_MakesAnIppPrinterForPort631()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(NetworkPrinterEndpoint.Ipp("printer.local")));

        _ = Assert.IsType<IppPrinter>(printer);
    }

    [Fact]
    public void Open_MakesARawPrinterForPort9100()
    {
        PrinterFactory factory = new();

        var printer = factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local")));

        _ = Assert.IsType<RawPrinter>(printer);
    }

    [Fact]
    public void Open_MakesASpoolerPrinterForASpoolerEndpoint()
    {
        PrinterFactory factory = new();
        var discovered = new DiscoveredPrinter(
            PrinterId.ForSpooler("Lobby"),
            new SpoolerPrinterEndpoint("Lobby"),
            new PrinterInfo(PrinterId.ForSpooler("Lobby"), "Lobby"));

        var printer = factory.Open(discovered);

        _ = Assert.IsType<SpoolerPrinter>(printer);
    }

    [Fact]
    public async Task OpenAsync_MakesASpoolerPrinterForASpoolerIdentifier()
    {
        PrinterFactory factory = new();

        var printer = await factory.OpenAsync(PrinterId.ForSpooler("Lobby"), TestContext.Current.CancellationToken);

        _ = Assert.IsType<SpoolerPrinter>(printer);
    }

    [Fact]
    public async Task OpenAsync_ThrowsForAnIdentityFormBecauseItNamesNoAddress()
    {
        PrinterFactory factory = new();

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenAsync(
            PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242")),
            TestContext.Current.CancellationToken));

        Assert.Contains("IPrinterManager", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_FallsBackToARawPrinterWhenIppDoesNotAnswer()
    {
        // Nothing binds 127.0.0.2, so port 631 is refused at once: no DNS, no network.
        PrinterFactory factory = new();

        var printer = await factory.OpenAsync(PrinterId.ForRaw("127.0.0.2"), TestContext.Current.CancellationToken);

        var raw = Assert.IsType<RawPrinter>(printer);
        var endpoint = Assert.IsType<NetworkPrinterEndpoint>(raw.Endpoint);
        Assert.Equal(NetworkPrinterEndpoint.DefaultPort, endpoint.Port);
    }

    [Fact]
    public void Dispose_DisposesAnOwnedClientAndKeepsASuppliedOne()
    {
        using HttpClient supplied = new();
        PrinterFactory owned = new();
        PrinterFactory borrowed = new(supplied);

        owned.Dispose();
        borrowed.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => owned.Open(Found(NetworkPrinterEndpoint.Ipp("printer.local"))));
        _ = Assert.Throws<ObjectDisposedException>(() => borrowed.Open(Found(NetworkPrinterEndpoint.Ipp("printer.local"))));
        // The supplied client is still usable after the factory that borrowed it is gone.
        Assert.Equal(TimeSpan.FromSeconds(100), supplied.Timeout);
        _ = supplied.DefaultRequestHeaders;
    }

    [Fact]
    public void Open_RawPrintersShareTheStatusClientsOfTheFactory()
    {
        PrinterFactory factory = new();

        var first = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local"))));
        var second = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("other.local"))));

        Assert.Same(first.IppStatusClient, second.IppStatusClient);
        Assert.Same(first.SnmpStatusClient, second.SnmpStatusClient);
    }
}
