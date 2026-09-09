using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Hands out one channel for each attempt the client makes. An attempt with no answers
// makes the client time out, so a test can drive the retry path. Each answer is built from
// the request identifier the client actually sent, the way a real agent echoes it.
internal sealed class FakeSnmpChannelFactory
{
    private readonly Queue<Func<int, byte[]>[]> _attempts;

    public FakeSnmpChannelFactory(params Func<int, byte[]>[][] attempts) =>
        _attempts = new Queue<Func<int, byte[]>[]>(attempts);

    public List<byte[]> Requests { get; } = [];

    public int AttemptCount { get; private set; }

    public IUdpChannel Create()
    {
        AttemptCount++;
        var answers = _attempts.Count > 0 ? _attempts.Dequeue() : [];
        return new FakeSnmpChannel(answers, Requests);
    }

    private sealed class FakeSnmpChannel : IUdpChannel
    {
        private readonly Func<int, byte[]>[] _answers;
        private readonly List<byte[]> _requests;
        private readonly Queue<byte[]> _ready = new();

        public FakeSnmpChannel(Func<int, byte[]>[] answers, List<byte[]> requests)
        {
            _answers = answers;
            _requests = requests;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = datagram.ToArray();
            _requests.Add(request);
            var requestId = SnmpRequests.ReadRequestId(request);
            foreach (var answer in _answers)
            {
                _ready.Enqueue(answer(requestId));
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_ready.Count > 0)
            {
                return new UdpReceiveResult(_ready.Dequeue(), new IPEndPoint(IPAddress.Loopback, 161));
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable.");
        }

        public void Dispose()
        {
        }
    }
}
