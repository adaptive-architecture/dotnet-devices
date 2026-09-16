using System.Net;

namespace AdaptArch.Devices.Printing;

// Counts the packets a browse or a walk could not read, and says which senders sent them.
//
// One broken device on a busy network can send thousands of unreadable packets in one
// browse. A warning for each of them would bury every other entry, so only the first few
// name a sender and the rest are counted. The address of the sender is what identifies the
// broken device, so it is in the entry that names it.
//
// One instance serves one browse or one walk. It is not thread-safe: each browse channel
// keeps its own.
internal sealed class MalformedPacketReporter
{
    private readonly HashSet<string> _senders = new(StringComparer.Ordinal);

    public int Count { get; private set; }

    public int SenderCount => _senders.Count;

    // True when this packet is one of the first few, so the caller names its sender.
    public bool ShouldReport(EndPoint? sender)
    {
        Count++;
        if (sender is not null)
        {
            _ = _senders.Add(sender.ToString() ?? String.Empty);
        }

        return Count <= DiscoveryLog.MaxMalformedReports;
    }

    // True when packets were skipped after the first few, so a summary is worth writing.
    public bool HasMore => Count > DiscoveryLog.MaxMalformedReports;
}
