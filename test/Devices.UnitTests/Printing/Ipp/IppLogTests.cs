using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppLogTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = NetworkPrinterEndpoint.Ipp("printer.local");

    [Fact]
    public async Task GetStatusAsync_WithALoggerFactory_RecordsTheProbeAndTheOperation()
    {
        var attributes = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        _ = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        // 1001 the probe answered, 1003 the endpoint resolved, 1011 the operation finished.
        Assert.Contains(1001, factory.EventIds);
        Assert.Contains(1003, factory.EventIds);
        Assert.Contains(1011, factory.EventIds);
        // Scoped to these ids: IppLog also carries a Warning for a format downgrade.
        Assert.All(factory.WithId(1001), entry => Assert.Equal(LogLevel.Debug, entry.Level));
        Assert.All(factory.WithId(1003), entry => Assert.Equal(LogLevel.Debug, entry.Level));
        Assert.All(factory.WithId(1011), entry => Assert.Equal(LogLevel.Debug, entry.Level));
        Assert.Contains("AdaptArch.Devices.Printing.Ipp", factory.Categories);
    }

    [Fact]
    public async Task GetStatusAsync_WithNoLoggerFactory_WritesNothing()
    {
        var attributes = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions());

        _ = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Empty(factory.Entries);
    }

    [Fact]
    public async Task ResolveAsync_EachProbeThatFailsIsRecordedWithItsCause()
    {
        IppMessages.StubHandler handler = new(_ => throw new HttpRequestException("Connection refused"));
        FakeLoggerFactory factory = new();
        IppEndpointResolver resolver = new(
            new IppContext(new HttpClient(handler), new IppTransportOptions { LoggerFactory = factory }),
            "printer.local",
            631,
            null);

        _ = await Assert.ThrowsAsync<PrinterConnectionException>(() => resolver.ResolveAsync(TestContext.Current.CancellationToken));

        // 1002 is a probe that did not answer: two schemes over two well-known paths.
        var probes = factory.WithId(1002);
        Assert.Equal(4, probes.Count);
        Assert.All(probes, entry => Assert.IsType<HttpRequestException>(entry.Exception));
    }

    [Fact]
    public async Task SendAsync_AFailedOperationIsRecordedWithItsStatusCode()
    {
        // 0x0400 is client-error-bad-request.
        var body = IppMessages.Response(0x0400, (0x23, "printer-state", 3));
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;

            // The first request is the resolver probe, which must answer.
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, (0x23, "printer-state", 3)))
                : IppMessages.Ok(body);
        });
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        _ = await Assert.ThrowsAsync<PrinterOperationException>(() => printer.GetStatusAsync(TestContext.Current.CancellationToken));

        var failure = Assert.Single(factory.WithId(1012));
        Assert.Contains("Get-Printer-Attributes", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1024", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobAsync_RecordsTheJobItRead()
    {
        var job = IppMessages.Response(
            0x0000,
            0x02,
            (0x21, "job-id", 7),
            (0x23, "job-state", 5),
            (0x44, "job-state-reasons", "resources-are-not-ready"));
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            return requestCount == 1
                ? IppMessages.Ok(IppMessages.Response(0x0000, (0x23, "printer-state", 3)))
                : IppMessages.Ok(job);
        });
        FakeLoggerFactory factory = new();
        IppPrintJobQueue queue = new(Endpoint, new HttpClient(handler), new IppTransportOptions { LoggerFactory = factory });

        _ = await queue.GetJobAsync(PrinterId.ForIpp("printer.local"), "7", TestContext.Current.CancellationToken);

        var read = Assert.Single(factory.WithId(1021));
        Assert.Contains("resources-are-not-ready", read.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_APrinterThatListsNoFormatIsAWarning()
    {
        // The cause of "my label printed as a page of source": the printer named no format
        // it knows, so the job goes as opaque bytes and a server may re-type them as text.
        var attributes = IppMessages.Response(0x0000, (0x23, "printer-state", 3));
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;

            // 1 is the probe, 2 the configuration read, 3 the submission.
            return requestCount <= 2 ? IppMessages.Ok(attributes) : IppMessages.Ok(job);
        });
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        var downgrade = Assert.Single(factory.WithId(1031));
        Assert.Equal(LogLevel.Warning, downgrade.Level);
        Assert.Contains(PrinterContentTypes.OctetStream, downgrade.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Debug, Assert.Single(factory.WithId(1030)).Level);
    }

    [Fact]
    public async Task PrintAsync_APrinterThatListsTheFormatIsNoWarning()
    {
        var attributes = IppMessages.Response(
            0x0000,
            (0x23, "printer-state", 3),
            (0x44, "document-format-supported", PrinterContentTypes.Zpl));
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            return requestCount <= 2 ? IppMessages.Ok(attributes) : IppMessages.Ok(job);
        });
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Empty(factory.WithId(1031));
    }
}
