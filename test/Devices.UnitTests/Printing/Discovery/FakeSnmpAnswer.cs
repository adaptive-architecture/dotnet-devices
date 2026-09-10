using System.Net;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// One datagram that the fake agent sends back. The answer is built from the request the
// client actually sent, so it can echo the request identifier the way a real agent does, or
// act on the identifiers and the repetition count of a GetBulkRequest.
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
