using System.Net;
using System.Text;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class IppPrinterStatusClientTests
{
    [Fact]
    public async Task GetDetailsAsync_ParsesStatusInfoAndMarkers()
    {
        byte[] response = BuildResponse(0x0000,
            (0x42, "printer-make-and-model", "EPSON L6270 Series"),
            (0x23, "printer-state", 3),
            (0x44, "printer-state-reasons", "none"),
            (0x22, "printer-is-accepting-jobs", (byte)1),
            (0x42, "marker-names", "Black ink"),
            (0x42, null, "Cyan ink"),
            (0x42, null, "Magenta ink"),
            (0x42, null, "Yellow ink"),
            (0x42, "marker-colors", "#000000"),
            (0x42, null, "#00FFFF"),
            (0x42, null, "#FF00FF"),
            (0x42, null, "#FFFF00"),
            (0x21, "marker-levels", 84),
            (0x21, null, 52),
            (0x21, null, 44),
            (0x21, null, -1));
        StubHandler handler = new(_ => IppOk(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        IppPrinterDetails details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal("printer.local", details.Info.Id.Value);
        Assert.Equal("EPSON L6270 Series", details.Info.Name);
        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.True(details.Status.IsAcceptingJobs);
        Assert.Null(details.Status.Detail);
        Assert.Equal(4, details.Status.Markers.Count);
        Assert.Equal("Black ink", details.Status.Markers[0].Name);
        Assert.Equal("#000000", details.Status.Markers[0].Color);
        Assert.Equal(84, details.Status.Markers[0].LevelPercent);
        Assert.Equal("Yellow ink", details.Status.Markers[3].Name);
        Assert.Null(details.Status.Markers[3].LevelPercent);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("application/ipp", request.Content.Headers.ContentType.MediaType);
    }

    [Fact]
    public async Task GetDetailsAsync_JoinsStateReasonsAndMapsProcessing()
    {
        byte[] response = BuildResponse(0x0000,
            (0x23, "printer-state", 4),
            (0x44, "printer-state-reasons", "media-empty"),
            (0x44, null, "toner-low"));
        IppPrinterStatusClient client = new(new HttpClient(new StubHandler(_ => IppOk(response))));

        IppPrinterDetails details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Processing, details.Status.State);
        Assert.Equal("media-empty; toner-low", details.Status.Detail);
        Assert.True(details.Status.IsAcceptingJobs);
        Assert.Equal("printer.local", details.Info.Name);
        Assert.Empty(details.Status.Markers);
    }

    [Fact]
    public async Task GetDetailsAsync_StoppedPrinterIsPausedAndNotAccepting()
    {
        byte[] response = BuildResponse(0x0000, (0x23, "printer-state", 5));
        IppPrinterStatusClient client = new(new HttpClient(new StubHandler(_ => IppOk(response))));

        IppPrinterDetails details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Paused, details.Status.State);
        Assert.False(details.Status.IsAcceptingJobs);
        Assert.Null(details.Status.Detail);
    }

    [Fact]
    public async Task GetDetailsAsync_FallsBackFromHttpsToHttp()
    {
        byte[] response = BuildResponse(0x0000, (0x23, "printer-state", 3));
        StubHandler handler = new(request => request.RequestUri.Scheme == "https"
            ? throw new HttpRequestException("TLS failure")
            : IppOk(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        IppPrinterDetails details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("https", handler.Requests[0].RequestUri.Scheme);
        Assert.Equal("https", handler.Requests[1].RequestUri.Scheme);
        Assert.Equal("http", handler.Requests[2].RequestUri.Scheme);
    }

    [Fact]
    public async Task GetDetailsAsync_FallsBackFromMissingResourcePath()
    {
        byte[] response = BuildResponse(0x0000, (0x23, "printer-state", 3));
        StubHandler handler = new(request => request.RequestUri.AbsolutePath == "/ipp/print"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : IppOk(response));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        IppPrinterDetails details = await client.GetDetailsAsync("printer.local", CancellationToken.None);

        Assert.Equal(PrinterStatusState.Idle, details.Status.State);
        Assert.Equal("/ipp/port1", handler.Requests[1].RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task GetDetailsAsync_IppErrorStatus_Throws()
    {
        byte[] response = BuildResponse(0x0400);
        IppPrinterStatusClient client = new(new HttpClient(new StubHandler(_ => IppOk(response))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetDetailsAsync("printer.local", CancellationToken.None));
    }

    [Fact]
    public async Task GetDetailsAsync_UnreachablePrinter_Throws()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("refused"));
        IppPrinterStatusClient client = new(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetDetailsAsync("printer.local", CancellationToken.None));

        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetDetailsAsync_CanceledToken_ThrowsOperationCanceled()
    {
        StubHandler handler = new(_ => IppOk(BuildResponse(0x0000)));
        IppPrinterStatusClient client = new(new HttpClient(handler));
        using CancellationTokenSource canceledSource = new();
        await canceledSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetDetailsAsync("printer.local", canceledSource.Token));

        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage IppOk(byte[] payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
    }

    private static byte[] BuildResponse(ushort status, params (byte Tag, string Name, object Value)[] attributes)
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
        buffer.WriteByte(0x04);
        foreach ((byte tag, string name, object value) in attributes)
        {
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
            else
            {
                throw new NotSupportedException();
            }
            byte[] nameBytes = name is null ? [] : Encoding.UTF8.GetBytes(name);
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

    private sealed class StubHandler : HttpMessageHandler
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
