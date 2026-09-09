using System.Net;
using System.Text;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Builds IPP response bytes by hand, so a test states exactly which attributes the
// printer sends back. The typed model of SharpIppNext cannot express a malformed or
// partial answer, which several tests need.
internal static class IppMessages
{
    public static HttpResponseMessage Ok(byte[] payload) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    // 0x04 is the printer-attributes-tag, which is what every printer-attribute test needs.
    public static byte[] Response(ushort status, params (byte Tag, string Name, object Value)[] attributes) =>
        Response(status, 0x04, attributes);

    // A job response needs its job-id and job-state inside the job-attributes-tag (0x02),
    // not the printer-attributes-tag, or SharpIppNext leaves PrintJobResponse.JobAttributes null.
    public static byte[] Response(ushort status, byte groupTag, params (byte Tag, string Name, object Value)[] attributes)
    {
        using MemoryStream buffer = new();
        buffer.WriteByte(1);
        buffer.WriteByte(1);
        buffer.WriteByte((byte)(status >> 8));
        buffer.WriteByte((byte)status);
        buffer.WriteByte(0);
        buffer.WriteByte(0);
        buffer.WriteByte(0);
        buffer.WriteByte(1);
        buffer.WriteByte(groupTag);
        foreach ((var tag, var name, var value) in attributes)
        {
            if (name is null && value is null)
            {
                // A bare tag with no name and no value is a new attribute-group boundary
                // (for example another 0x02 job-attributes-tag), not a 1setOf continuation
                // value (name is null, value is not, elsewhere in this file). SharpIppNext
                // groups a GetJobsResponse into one job per group tag: a repeated attribute
                // name inside a single group does not start a new job, it just gets
                // dropped, so a multi-job answer needs one of these between each job's
                // attributes.
                buffer.WriteByte(tag);
                continue;
            }

            byte[] valueBytes;
            if (value is string text)
            {
                valueBytes = Encoding.UTF8.GetBytes(text);
            }
            else if (value is int number)
            {
                valueBytes = [(byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number];
            }
            else if (value is byte flag)
            {
                valueBytes = [flag];
            }
            else if (value is byte[] raw)
            {
                // A resolution value is pre-encoded by the caller: width, height (4 bytes
                // each, big-endian) and a 1-byte unit, per RFC 8010.
                valueBytes = raw;
            }
            else
            {
                throw new NotSupportedException();
            }
            var nameBytes = name is null ? [] : Encoding.UTF8.GetBytes(name);
            buffer.WriteByte(tag);
            buffer.WriteByte((byte)(nameBytes.Length >> 8));
            buffer.WriteByte((byte)nameBytes.Length);
            buffer.Write(nameBytes, 0, nameBytes.Length);
            buffer.WriteByte((byte)(valueBytes.Length >> 8));
            buffer.WriteByte((byte)valueBytes.Length);
            buffer.Write(valueBytes, 0, valueBytes.Length);
        }

        buffer.WriteByte(0x03);
        return buffer.ToArray();
    }

    public static async Task<PrinterDescriptionAttributes> DecodePrinterAttributesAsync(byte[] body)
    {
        Uri uri = new("ipp://printer.local:631/ipp/print");
        using SharpIppClient client = new(new HttpClient(new StubHandler(_ => Ok(body))), new IppProtocol());
        var response = await client.GetPrinterAttributesAsync(
            new GetPrinterAttributesRequest { OperationAttributes = new() { PrinterUri = uri } },
            CancellationToken.None).ConfigureAwait(false);
        return response.PrinterAttributes;
    }

    internal sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }
}
