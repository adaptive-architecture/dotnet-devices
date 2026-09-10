using System.Net;
using System.Text;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp;
using SharpIpp.Models.Requests;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;

namespace AdaptArch.Devices.UnitTests.Printing.Ipp;

// Builds IPP response bytes by hand: the typed SharpIppNext model cannot express the
// malformed or partial answers several tests need.
internal static class IppMessages
{
    public static HttpResponseMessage Ok(byte[] payload) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    // 0x04 is the printer-attributes-tag.
    public static byte[] Response(ushort status, params (byte Tag, string Name, object Value)[] attributes) =>
        Response(status, 0x04, attributes);

    // A job response needs job-id and job-state inside the job-attributes-tag (0x02), or
    // SharpIppNext leaves PrintJobResponse.JobAttributes null.
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
                // A bare tag starts a new attribute group. SharpIppNext makes one job per
                // group, so a multi-job answer needs one of these between the jobs.
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
                // The caller pre-encoded the resolution.
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

    // The raw response as well, for an attribute the typed model does not carry.
    public static async Task<(PrinterDescriptionAttributes Attributes, IIppResponseMessage Raw)> DecodeWithRawAsync(byte[] body)
    {
        Uri uri = new("ipp://printer.local:631/ipp/print");
        IppOperations operations = new(new HttpClient(new StubHandler(_ => Ok(body))));
        var response = await operations.SendAsync(
            static (client, request, token) => client.GetPrinterAttributesAsync(request, token),
            new GetPrinterAttributesRequest { OperationAttributes = new() { PrinterUri = uri } },
            uri,
            CancellationToken.None).ConfigureAwait(false);
        return (response.PrinterAttributes, operations.LastRawResponse);
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
