using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Replays a queue of datagrams, then blocks until the caller's token is cancelled.
internal sealed class FakeUdpChannel : IUdpChannel
{
    private readonly Queue<byte[]> _answers;

    public FakeUdpChannel(params byte[][] answers) => _answers = new Queue<byte[]>(answers);

    public List<byte[]> Sent { get; } = [];

    public bool Disposed { get; private set; }

    public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Sent.Add(datagram.ToArray());
        return ValueTask.CompletedTask;
    }

    public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_answers.Count > 0)
        {
            return new UdpReceiveResult(_answers.Dequeue(), new IPEndPoint(IPAddress.Loopback, 5353));
        }

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Unreachable.");
    }

    public void Dispose() => Disposed = true;
}
