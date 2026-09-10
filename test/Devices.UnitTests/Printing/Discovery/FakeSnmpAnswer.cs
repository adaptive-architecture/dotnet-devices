using System.Net;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// One datagram the fake agent sends back, built from the request the client sent.
internal sealed class FakeSnmpAnswer
{
    public FakeSnmpAnswer(Func<byte[], byte[]> answer, IPAddress sender = null)
    {
        Answer = answer;
        Sender = sender;
    }

    public Func<byte[], byte[]> Answer { get; }

    // The address the datagram comes from. Null means the address the request went to.
    public IPAddress Sender { get; }
}
