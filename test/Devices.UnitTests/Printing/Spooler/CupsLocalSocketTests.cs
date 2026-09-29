using System.Net.Sockets;
using System.Text;
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using AdaptArch.Devices.Printing.Spooler;
using AdaptArch.Devices.UnitTests.Printing.Ipp;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public sealed class CupsLocalSocketTests : IDisposable
{
    // Short on purpose: a socket path is limited to about 104 bytes on macOS.
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cups-{Guid.NewGuid():N}.sock");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Find_ReturnsTheFirstCandidateThatExists()
    {
        File.WriteAllBytes(_path, []);
        var missing = Path.Combine(Path.GetTempPath(), $"cups-{Guid.NewGuid():N}.missing");

        Assert.Equal(_path, CupsLocalSocket.Find([missing, _path]));
    }

    [Fact]
    public void Find_ReturnsNullWhenNoCandidateExists() =>
        Assert.Null(CupsLocalSocket.Find([_path]));

    [Fact]
    public async Task ConnectAsync_ReturnsAStreamToTheListeningSocket()
    {
        var token = TestContext.Current.CancellationToken;
        using var listener = Listen();
        var accept = listener.AcceptAsync(token).AsTask();

        await using var stream = await CupsLocalSocket.ConnectAsync(_path, token);
        using var server = await accept;
        await stream.WriteAsync("ping"u8.ToArray(), token);
        var buffer = new byte[4];
        await server.ReceiveAsync(buffer, SocketFlags.None, token);

        Assert.Equal("ping", Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public async Task ConnectAsync_ThrowsWhenNothingListens() =>
        await Assert.ThrowsAnyAsync<SocketException>(
            async () => await CupsLocalSocket.ConnectAsync(_path, TestContext.Current.CancellationToken));

    // The case the socket exists for: the driver still names ipp://localhost:631/, and
    // the request reaches the daemon through the socket, where no TCP listener exists.
    [Fact]
    public async Task EnumeratePrintersAsync_ReachesTheDaemonThroughTheSocket()
    {
        var token = TestContext.Current.CancellationToken;
        using var listener = Listen();
        var body = IppMessages.Response(0x0000, (0x42, "printer-name", "lobby"));
        var serve = ServeOneAsync(listener, body, token);

        using var client = IppHttpClientFactory.CreateCore(new IppTransportOptions(), _path);
        CupsSpoolerDriver driver = new(client);
        var printers = await driver.EnumeratePrintersAsync(token);
        var request = await serve;

        Assert.Equal("lobby", Assert.Single(printers).Id.Authority);
        Assert.StartsWith("POST / HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Host: localhost:631", request, StringComparison.OrdinalIgnoreCase);
    }

    private Socket Listen()
    {
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(_path));
        socket.Listen(1);
        return socket;
    }

    // A one-shot HTTP server: reads the request head, answers with the IPP body and closes.
    // The request body is not read; the answer is the same whatever it holds.
    private static async Task<string> ServeOneAsync(Socket listener, byte[] body, CancellationToken token)
    {
        using var connection = await listener.AcceptAsync(token).ConfigureAwait(false);
        await using NetworkStream stream = new(connection, false);
        StringBuilder head = new();
        var buffer = new byte[4096];
        while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            head.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var status = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/ipp\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(status, token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        return head.ToString();
    }
}
