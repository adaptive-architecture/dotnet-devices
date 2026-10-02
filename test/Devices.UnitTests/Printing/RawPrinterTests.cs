using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.UnitTests.Printing.Discovery;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
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
        RawPrinter printer = new(NetworkPrinterEndpoint.Raw("127.0.0.1", port));

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
        RawPrinter printer = new(NetworkPrinterEndpoint.Raw("127.0.0.1"));

        var configuration = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.Empty(configuration.MediaSizes);
        Assert.Empty(configuration.SupportedResolutionsDpi);
        Assert.Null(configuration.SupportsDuplex);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsUnknownWhenNeitherSnmpNorIppAnswer()
    {
        // An SNMP channel that never answers and an IPP endpoint that refuses, so nothing
        // leaves the process and the single attempt ends at its short timeout.
        SnmpPrinterStatusOptions snmpOptions = new() { RequestTimeout = TimeSpan.FromMilliseconds(50), Retries = 0 };
        SnmpPrinterStatusClient snmp = new(snmpOptions, new FakeSnmpChannelFactory().Create);
        IppPrinterStatusClient ipp = new(new HttpClient(new IppMessages.StubHandler(_ => throw new HttpRequestException("refused"))));
        RawPrinter printer = new(NetworkPrinterEndpoint.Raw("192.0.2.1"), snmp, ipp);

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Unknown, status.State);
    }
}
