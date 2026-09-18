#nullable enable
using System.Text;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.IntegrationTests.Fixtures;
using AdaptArch.Devices.Printing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdaptArch.Devices.IntegrationTests;

/// <summary>
/// The raw channel, end to end over a real socket: registration, the TCP probe, the device
/// grouping, the factory and the transport. The unit tests reach the transport directly;
/// what these prove is that the whole path above it agrees on one printer.
/// </summary>
public class RawPrintingTests
{
    [Fact]
    public async Task PrintAsync_SendsTheBytesTheCallerGave()
    {
        await using RawTestPrinter printer = new();
        await using var provider = BuildProvider(printer.Port);
        var manager = provider.GetRequiredService<IPrinterManager>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var devices = await manager.DiscoverAsync(null, cancellationToken);

        var device = Assert.Single(devices);
        Assert.Equal(PrinterScheme.Raw, device.Id.Scheme);
        Assert.Equal(printer.Port, device.Id.Port);

        var job = await manager.PrintAsync(
            device.Id,
            PrinterPayload.FromString("^XA^FO50,50^ADN,36,20^FDhello^FS^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { JobName = "raw-end-to-end" },
            cancellationToken);

        var received = await printer.NextJobAsync(cancellationToken);
        Assert.Equal("^XA^FO50,50^ADN,36,20^FDhello^FS^XZ", Encoding.UTF8.GetString(received));

        // The raw channel reports no identifier of its own, so the library synthesizes one
        // and calls the job done as soon as the bytes are on the wire.
        Assert.Equal(PrintJobState.Completed, job.State);
        Assert.NotEmpty(job.JobId);
        Assert.Equal("raw-end-to-end", job.JobName);
    }

    [Fact]
    public async Task PrintAsync_SendsBinaryPayloadsUnchanged()
    {
        var payload = new byte[256];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)index;
        }

        await using RawTestPrinter printer = new();
        await using var provider = BuildProvider(printer.Port);
        var manager = provider.GetRequiredService<IPrinterManager>();
        var cancellationToken = TestContext.Current.CancellationToken;

        _ = await manager.PrintAsync(
            PrinterId.ForRaw("127.0.0.1", printer.Port),
            PrinterPayload.FromBytes(payload, PrinterContentTypes.EscPos),
            null,
            cancellationToken);

        var received = await printer.NextJobAsync(cancellationToken);
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsNothingWhenTheProbedPortIsClosed()
    {
        int closedPort;
        await using (RawTestPrinter printer = new())
        {
            closedPort = printer.Port;
        }

        await using var provider = BuildProvider(closedPort);
        var manager = provider.GetRequiredService<IPrinterManager>();

        var devices = await manager.DiscoverAsync(null, TestContext.Current.CancellationToken);

        Assert.Empty(devices);
    }

    [Fact]
    public async Task PrintAsync_UnreachablePort_ReportsTheFailureInsteadOfSwallowingIt()
    {
        int closedPort;
        await using (RawTestPrinter printer = new())
        {
            closedPort = printer.Port;
        }

        await using var provider = BuildProvider(closedPort);
        var manager = provider.GetRequiredService<IPrinterManager>();

        _ = await Assert.ThrowsAnyAsync<Exception>(() => manager.PrintAsync(
            PrinterId.ForRaw("127.0.0.1", closedPort),
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken));
    }

    private static ServiceProvider BuildProvider(int port)
    {
        ServiceCollection services = new();
        _ = services.AddPrinters(configureManager: options =>
        {
            options.IncludeMdns = false;
            options.IncludeSpooler = false;
            options.Transports = [PrinterScheme.Raw];
            options.Probe = new NetworkPrinterDiscoveryOptions
            {
                Hosts = ["127.0.0.1"],
                Port = port,
                Scheme = PrinterScheme.Raw,
                ConnectTimeout = TimeSpan.FromSeconds(2),
            };
        });

        return services.BuildServiceProvider();
    }
}
