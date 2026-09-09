using System.Net;
using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

public class IppPrinterTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = new("printer.local", 631);

    [Fact]
    public async Task PrintAsync_ReturnsTheJobIdentifierThePrinterAssigned()
    {
        // 0x21 is integer; 0x23 is enum. Job state 3 is pending. 0x02 is the job-attributes group.
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 42), (0x23, "job-state", 3));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(job));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var result = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("42", result.JobId);
        Assert.Equal(PrintJobState.Queued, result.State);
        Assert.Empty(result.DroppedOptions);
    }

    [Fact]
    public async Task PrintAsync_ThrowRejectsAnUnsupportedOptionBeforeItPrints()
    {
        var attributes = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0),
            (0x44, "media-supported", "iso_a4_210x297mm"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await Assert.ThrowsAsync<NotSupportedException>(() => printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Throw },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PrintAsync_DocumentFormatComesFromThePayloadContentType()
    {
        // 0x02 is the job-attributes group.
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 1), (0x23, "job-state", 3));
        CapturingHandler handler = new(job);
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        _ = await printer.PrintAsync(
            PrinterPayload.FromString("%PDF-1.4", PrinterContentTypes.Pdf),
            null,
            TestContext.Current.CancellationToken);

        // The print submission is the last captured request. The body is read inside the
        // handler, while the request's document stream is still open, because a real
        // transport reads it during the send and IppPrinter disposes it right after.
        var text = Encoding.Latin1.GetString(handler.RequestBodies[^1]);
        Assert.Contains(PrinterContentTypes.Pdf, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrintAsync_DropNamesTheRemovedOptionOnTheReturnedJob()
    {
        var attributes = IppMessages.Response(0x0000,
            (0x44, "sides-supported", "one-sided"),
            (0x22, "color-supported", (byte)0),
            (0x44, "media-supported", "iso_a4_210x297mm"));
        // 0x02 is the job-attributes group.
        var job = IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 7), (0x23, "job-state", 3));
        var requestCount = 0;
        IppMessages.StubHandler handler = new(_ =>
        {
            requestCount++;
            // Request 1 is the resolver probe, request 2 is the configuration read, both
            // against printer attributes. Request 3 is the print submission.
            return requestCount <= 2 ? IppMessages.Ok(attributes) : IppMessages.Ok(job);
        });
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var result = await printer.PrintAsync(
            PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl),
            new PrintOptions { Duplex = DuplexMode.LongEdge, OnUnsupported = UnsupportedOptionBehavior.Drop },
            TestContext.Current.CancellationToken);

        Assert.Equal("7", result.JobId);
        Assert.Equal(PrintJobState.Queued, result.State);
        Assert.Equal([nameof(PrintOptions.Duplex)], result.DroppedOptions);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReadsOneTimeAndKeepsTheAnswer()
    {
        var attributes = IppMessages.Response(0x0000, (0x22, "color-supported", (byte)1));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var first = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);
        var second = await printer.GetConfigurationAsync(TestContext.Current.CancellationToken);

        Assert.True(first.SupportsColor);
        Assert.Same(first, second);
        // One probe from the resolver and one attribute read. No second read.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsTheState()
    {
        var attributes = IppMessages.Response(0x0000,
            (0x23, "printer-state", 4),
            (0x44, "printer-state-reasons", "media-empty"));
        IppMessages.StubHandler handler = new(_ => IppMessages.Ok(attributes));
        using IppPrinter printer = new(Endpoint, new HttpClient(handler));

        var status = await printer.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PrinterStatusState.Processing, status.State);
        Assert.Equal("media-empty", status.Detail);
    }

    // IppMessages.StubHandler never reads a request's content, which is fine for every other
    // test here because none of them need it. This one does, and it must read it before
    // returning, because IppPrinter disposes the document stream right after the send.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly byte[] _responseBody;

        public CapturingHandler(byte[] responseBody) => _responseBody = responseBody;

        public List<byte[]> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            }

            return IppMessages.Ok(_responseBody);
        }
    }
}
