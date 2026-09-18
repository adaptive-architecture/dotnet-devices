#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace AdaptArch.Devices.IntegrationTests.Fixtures;

/// <summary>
/// A raw print server on the loopback interface: it accepts every connection, reads it to
/// the end and keeps the bytes. The TCP probe of the discovery connects and sends nothing,
/// so an empty connection is counted and dropped rather than queued as a job.
/// </summary>
internal sealed class RawTestPrinter : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Channel<byte[]> _jobs = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _accepting;
    private int _connections;

    public RawTestPrinter()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = AcceptLoopAsync(_stopping.Token);
    }

    public int Port { get; }

    public int Connections => Volatile.Read(ref _connections);

    public async Task<byte[]> NextJobAsync(CancellationToken cancellationToken) =>
        await _jobs.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        try
        {
            await _accepting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The listener was stopped on purpose.
        }

        _stopping.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Interlocked.Increment(ref _connections);
                _ = ReadAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (SocketException)
        {
            // The listener was closed under the pending accept.
        }
    }

    private async Task ReadAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            {
                await using var stream = client.GetStream();
                await using MemoryStream buffer = new();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (buffer.Length > 0)
                {
                    await _jobs.Writer.WriteAsync(buffer.ToArray(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // A probe that closes the connection without writing is not a job.
        }
    }
}
