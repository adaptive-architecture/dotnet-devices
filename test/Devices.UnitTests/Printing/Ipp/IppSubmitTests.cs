using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Proves what a submit records about the upload. A printer answers when it accepts
// the job, which may be before it read the whole document: an early answer aborts
// the rest without an error, and only the counted octets can still tell.
public class IppSubmitTests
{
    private static readonly NetworkPrinterEndpoint Endpoint = NetworkPrinterEndpoint.Ipp("printer.local");

    // 0x02 is the job-attributes group: a job answer populates from no other tag.
    private static byte[] JobResponse() =>
        IppMessages.Response(0x0000, 0x02, (0x21, "job-id", 5), (0x23, "job-state", 3));

    private static byte[] AttributesResponse() =>
        IppMessages.Response(0x0000, (0x23, "printer-state", 3));

    [Fact]
    public async Task PrintAsync_RecordsTheSubmittedOctets()
    {
        // Five octets of octet-stream: no negotiation, so the print is the only job.
        PeekingHandler handler = new(int.MaxValue);
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("hello", PrinterContentTypes.OctetStream),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("5", job.JobId);
        var submitted = Assert.Single(factory.WithId(1035));
        Assert.Equal(LogLevel.Debug, submitted.Level);
        Assert.Contains("5 octets", submitted.Message, StringComparison.Ordinal);
        Assert.Empty(factory.WithId(1036));
    }

    [Fact]
    public async Task PrintAsync_ReportsAJobThePrinterAnsweredBeforeReadingWhole()
    {
        // The handler reads the four octets the operation identifier needs and then
        // answers the Print-Job. SharpIppNext concatenates its framing with the
        // document, so peeking the framing never touches the document stream: the
        // upload stands at none of five octets, the way a printer does that
        // accepts the job before reading any of it. The job exists, so it is
        // returned; the short upload is reported and not thrown.
        PeekingHandler handler = new(4);
        FakeLoggerFactory factory = new();
        using IppPrinter printer = new(Endpoint, new HttpClient(handler), null, new IppTransportOptions { LoggerFactory = factory });

        var job = await printer.PrintAsync(
            PrinterPayload.FromString("hello", PrinterContentTypes.OctetStream),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal("5", job.JobId);
        var shortSent = Assert.Single(factory.WithId(1036));
        Assert.Equal(LogLevel.Error, shortSent.Level);
        Assert.Contains("0 of 5 octets", shortSent.Message, StringComparison.Ordinal);
    }

    // Answers by IPP operation after reading only what it is told to. Reading
    // everything stands in for a transport that delivers the whole document;
    // reading the four octets of the operation header stands in for a printer
    // that accepts the job before the upload finished.
    private sealed class PeekingHandler : HttpMessageHandler
    {
        private const int GetPrinterAttributes = 0x000B;
        private const int PrintJob = 0x0002;

        private readonly int _contentBytesToRead;

        public PeekingHandler(int contentBytesToRead) => _contentBytesToRead = contentBytesToRead;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await using var content = await request.Content!.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var head = new byte[Math.Min(4, _contentBytesToRead)];
            var read = 0;
            while (read < head.Length)
            {
                var count = await content.ReadAsync(head.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            if (_contentBytesToRead == int.MaxValue)
            {
                await content.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
            }

            // RFC 8010: version (2 bytes), then the operation identifier (2 bytes).
            var operation = (head[2] << 8) | head[3];
            if (operation == PrintJob)
            {
                return IppMessages.Ok(JobResponse());
            }

            if (operation == GetPrinterAttributes)
            {
                return IppMessages.Ok(AttributesResponse());
            }

            throw new NotSupportedException($"The test handler received operation 0x{operation:X4}.");
        }
    }
}
