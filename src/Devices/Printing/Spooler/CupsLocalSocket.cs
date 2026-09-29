using System.IO;
using System.Net.Sockets;
using AdaptArch.Devices.Printing.Ipp;

namespace AdaptArch.Devices.Printing.Spooler;

// The local daemon is reached through its domain socket when it has one, and on
// localhost:631 when it does not. macOS starts cupsd on demand: launchd watches only the
// domain socket, and the TCP listener exists only while the daemon runs, so a cold
// connection to localhost:631 is refused. The socket is also what lpstat and every CUPS
// client use, so it is the one address a stock install always answers on.
internal static class CupsLocalSocket
{
    // macOS first, then the systemd and older Linux locations.
    private static readonly string[] Candidates =
    [
        "/private/var/run/cupsd",
        "/run/cups/cups.sock",
        "/var/run/cups/cups.sock",
    ];

    public static HttpClient CreateClient(IppTransportOptions options) =>
        IppHttpClientFactory.CreateCore(options, Find());

    // Null where no candidate exists, and the client then connects over TCP as usual.
    internal static string? Find() => Find(Candidates);

    internal static string? Find(IEnumerable<string> candidates) => candidates.FirstOrDefault(File.Exists);

    internal static async ValueTask<Stream> ConnectAsync(string path, CancellationToken cancellationToken)
    {
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
