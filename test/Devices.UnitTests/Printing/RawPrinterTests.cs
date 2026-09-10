using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class RawPrinterTests
{
    [Fact]
    public async Task PrintAsync_ReportsACompletedJobWithAGeneratedIdentifier()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));
        var acceptTask = listener.AcceptTcpClientAsync(timeoutSource.Token);
        RawPrinter printer = new(new NetworkPrinterEndpoint("127.0.0.1", port));

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            timeoutSource.Token);
        using var accepted = await acceptTask;

        Assert.NotEmpty(job.JobId);
        // The raw channel gives no job identifier, so the job is done when the bytes are out.
        Assert.Equal(PrintJobState.Completed, job.State);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReportsNothingKnown()
    {
        RawPrinter printer = new(new NetworkPrinterEndpoint("127.0.0.1", 9100));

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Null(configuration.SupportsDuplex);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsUnknownWhenNeitherSnmpNorIppAnswer()
    {
        // Nothing binds 127.0.0.4, so both probes fail fast. The short SNMP timeout and
        // single attempt save the six seconds the default options would take.
        SnmpPrinterStatusOptions snmpOptions = new() { RequestTimeout = TimeSpan.FromMilliseconds(200), Retries = 0 };
        RawPrinter printer = new(new NetworkPrinterEndpoint("127.0.0.4", 9100), snmpOptions);

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Unknown, status.State);
    }
}
