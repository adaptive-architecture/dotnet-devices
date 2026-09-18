using System.Net;
using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class TcpPrinterTransportTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task WriteAsync_TransmitsPayloadToListener()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));

        var readTask = ReadOnceAsync(listener, timeoutSource.Token);
        TcpPrinterTransport transport = new();
        var endpoint = NetworkPrinterEndpoint.Raw("127.0.0.1", port);
        var payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        await transport.WriteAsync(endpoint, payload, timeoutSource.Token);
        var received = await readTask;

        Assert.Equal("^XA^XZ", Encoding.UTF8.GetString(received));
    }

    [Fact]
    public void CanHandle_AcceptsOnlyNetworkEndpoints()
    {
        TcpPrinterTransport transport = new();

        Assert.True(transport.CanHandle(NetworkPrinterEndpoint.Raw("host")));
        Assert.False(transport.CanHandle(new SpoolerPrinterEndpoint("Q")));
    }

    [Fact]
    public async Task WriteAsync_UnsupportedEndpoint_ThrowsNotSupported()
    {
        TcpPrinterTransport transport = new();
        var payload = PrinterPayload.FromBytes(new byte[] { 0x00 }, PrinterContentTypes.OctetStream);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            transport.WriteAsync(new SpoolerPrinterEndpoint("Q"), payload, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_CanceledToken_ThrowsOperationCanceled()
    {
        TcpPrinterTransport transport = new();
        var payload = PrinterPayload.FromBytes(new byte[] { 0x00 }, PrinterContentTypes.OctetStream);
        using CancellationTokenSource canceledSource = new();
        await canceledSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.WriteAsync(NetworkPrinterEndpoint.Raw("127.0.0.1"), payload, canceledSource.Token));
    }

    // The listener never reads, and the payload is larger than both socket buffers.
    //
    // Skipped on Windows, where it proves nothing: the loopback stack buffers all 32 MB, so
    // the write completes and there is no wait to time out. Against a real printer the
    // buffers fill and the timeout does its work, on every operating system. Picking a size
    // that defeats Windows send-buffer autotuning would be a guess, and a test that passes
    // because the guess held is worse than one that says it does not apply here.
    [Fact(SkipWhen = nameof(OnWindows), Skip = "The Windows loopback stack buffers the whole payload, so the write never waits.")]
    public async Task WriteAsync_PrinterStopsReading_ThrowsTimeout()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(30));
        var acceptTask = listener.AcceptTcpClientAsync(timeoutSource.Token);

        TcpPrinterTransport transport = new(TimeSpan.FromMilliseconds(200));
        var payload = PrinterPayload.FromBytes(new byte[32 * 1024 * 1024], PrinterContentTypes.OctetStream);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.WriteAsync(NetworkPrinterEndpoint.Raw("127.0.0.1", port), payload, timeoutSource.Token));

        using var accepted = await acceptTask;
    }

    private static async Task<byte[]> ReadOnceAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        // TcpClient has no DisposeAsync, so it keeps the plain using.
        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        await using MemoryStream buffer = new();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
