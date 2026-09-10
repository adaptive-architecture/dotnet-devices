using System.Net;
using System.Net.Sockets;
using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// One channel per attempt. An attempt with no answers makes the client time out, so a
// test can drive the retry path.
internal sealed class FakeSnmpChannelFactory
{
    private readonly Queue<FakeSnmpAnswer[]> _attempts;

    public FakeSnmpChannelFactory(params FakeSnmpAnswer[][] attempts) =>
        _attempts = new Queue<FakeSnmpAnswer[]>(attempts);

    public List<byte[]> Requests { get; } = [];

    public List<AddressFamily> AddressFamilies { get; } = [];

    public int AttemptCount => AddressFamilies.Count;

    // When set, every send fails with this error.
    public SocketException SendFailure { get; set; }

    public IUdpChannel Create(AddressFamily addressFamily)
    {
        AddressFamilies.Add(addressFamily);
        var answers = _attempts.Count > 0 ? _attempts.Dequeue() : [];
        return new FakeSnmpChannel(answers, Requests, SendFailure);
    }

    private sealed class FakeSnmpChannel : IUdpChannel
    {
        private readonly FakeSnmpAnswer[] _answers;
        private readonly List<byte[]> _requests;
        private readonly SocketException _sendFailure;
        private readonly Queue<UdpReceiveResult> _ready = new();

        public FakeSnmpChannel(FakeSnmpAnswer[] answers, List<byte[]> requests, SocketException sendFailure)
        {
            _answers = answers;
            _requests = requests;
            _sendFailure = sendFailure;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sendFailure is not null)
            {
                throw _sendFailure;
            }

            var request = datagram.ToArray();
            _requests.Add(request);
            foreach (var answer in _answers)
            {
                IPEndPoint sender = new(answer.Sender ?? destination.Address, destination.Port);
                _ready.Enqueue(new UdpReceiveResult(answer.Answer(request), sender));
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_ready.Count > 0)
            {
                return _ready.Dequeue();
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("Unreachable.");
        }

        public void Dispose()
        {
        }
    }
}
