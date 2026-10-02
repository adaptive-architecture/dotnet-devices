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

    // Without it a raw printer opened through the manager has no log, so the events of the
    // raw channel could never fire.
    [Fact]
    public void Open_HandsItsLoggerFactoryToEveryPrinter()
    {
        FakeLoggerFactory log = new();
        using PrinterFactory factory = new() { LoggerFactory = log };

        var raw = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local"))));
        var spooler = Assert.IsType<SpoolerPrinter>(factory.Open(Found(new SpoolerPrinterEndpoint("Lobby"))));
        using var ipp = Assert.IsType<IppPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Ipp("printer.local"))));
        var cups = Assert.IsType<CupsPrinter>(factory.Open(Found(new CupsPrinterEndpoint("cups.local", "Lobby"))));

        Assert.Same(log, raw.LoggerFactory);
        Assert.Same(log, spooler.LoggerFactory);
        Assert.Same(log, ipp.LoggerFactory);
        Assert.Same(log, cups.LoggerFactory);
    }

    [Fact]
    public void Open_HandsItsFormatsToARawPrinter()
    {
        PrintFormatPolicy formats = new([new PrinterFormat("image/x-label", PrinterFormatKind.Image)], []);
        using PrinterFactory factory = new() { Formats = formats };

        var raw = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local"))));

        Assert.Same(formats, raw.Formats);
    }

    [Fact]
    public void Open_FallsBackToTheLoggerFactoryOfTheTransport()
    {
        FakeLoggerFactory log = new();
        using PrinterFactory factory = new(new IppTransportOptions { LoggerFactory = log });

        var raw = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local"))));

        Assert.Same(log, raw.LoggerFactory);
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

    [Fact]
    public async Task Open_RawPrintersUseTheTransportAndTheClientsOfTheFactory()
    {
        RecordingTransport transport = new();
        SnmpPrinterStatusClient snmp = new();
        using IppPrinterStatusClient ipp = new();
        PrinterFactory factory = new() { Transport = transport, SnmpStatusClient = snmp, IppStatusClient = ipp };

        var printer = Assert.IsType<RawPrinter>(factory.Open(Found(NetworkPrinterEndpoint.Raw("printer.local"))));
        _ = await printer.PrintAsync(PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl), null, TestContext.Current.CancellationToken);

        Assert.Same(snmp, printer.SnmpStatusClient);
        Assert.Same(ipp, printer.IppStatusClient);
        Assert.Equal(PrinterContentTypes.Zpl, Assert.Single(transport.Written).ContentType);
    }

    private sealed class RecordingTransport : IPrinterTransport
    {
        public List<PrinterPayload> Written { get; } = [];

        public bool CanHandle(PrinterEndpoint endpoint) => true;

        public Task WriteAsync(PrinterEndpoint endpoint, PrinterPayload payload, CancellationToken cancellationToken)
        {
            Written.Add(payload);
            return Task.CompletedTask;
        }
    }
}
