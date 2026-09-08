using System.Net;
using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class TcpPrinterTransportTests
{
    [Fact]
    public async Task WriteAsync_TransmitsPayloadToListener()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeoutSource = new(TimeSpan.FromSeconds(10));

        Task<byte[]> readTask = ReadOnceAsync(listener, timeoutSource.Token);
        TcpPrinterTransport transport = new();
        NetworkPrinterEndpoint endpoint = new("127.0.0.1", port);
        PrinterPayload payload = PrinterPayload.FromString("^XA^XZ", PrinterContentTypes.Zpl);

        await transport.WriteAsync(endpoint, payload, timeoutSource.Token);
        byte[] received = await readTask;

        Assert.Equal("^XA^XZ", Encoding.UTF8.GetString(received));
    }

    [Fact]
    public void CanHandle_AcceptsOnlyNetworkEndpoints()
    {
        TcpPrinterTransport transport = new();

        Assert.True(transport.CanHandle(new NetworkPrinterEndpoint("host")));
        Assert.False(transport.CanHandle(new UsbPrinterEndpoint(1, 2)));
        Assert.False(transport.CanHandle(new SpoolerPrinterEndpoint("Q")));
    }

    [Fact]
    public async Task WriteAsync_UnsupportedEndpoint_ThrowsNotSupported()
    {
        TcpPrinterTransport transport = new();
        PrinterPayload payload = PrinterPayload.FromBytes(new byte[] { 0x00 }, PrinterContentTypes.OctetStream);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            transport.WriteAsync(new UsbPrinterEndpoint(1, 2), payload, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_CanceledToken_ThrowsOperationCanceled()
    {
        TcpPrinterTransport transport = new();
        PrinterPayload payload = PrinterPayload.FromBytes(new byte[] { 0x00 }, PrinterContentTypes.OctetStream);
        using CancellationTokenSource canceledSource = new();
        await canceledSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            transport.WriteAsync(new NetworkPrinterEndpoint("127.0.0.1", 9100), payload, canceledSource.Token));
    }

    private static async Task<byte[]> ReadOnceAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        using NetworkStream stream = client.GetStream();
        using MemoryStream buffer = new();
        byte[] chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
