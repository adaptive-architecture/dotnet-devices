using System.Net;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

// The log of the discovery sources. Read the note on event identifiers in PrintingLog.
//
// The category is a filter name, not a namespace: the files of this folder share the
// namespace of the printing types, and the category is kept apart so a reader can turn the
// discovery log on and off by itself.
//
// Blocks: 3000 mDNS, 3010 SNMP, 3020 the network probe, 3030 the queue correlator,
// 3040 the grouper.
internal static partial class DiscoveryLog
{
    public const string Category = "AdaptArch.Devices.Printing.Discovery";

    public static ILogger Create(ILoggerFactory? factory) => DeviceLog.Create(factory, Category);

    // A broken device can send thousands of unreadable packets in one browse, so only the
    // first few are named and the rest are counted. The address of the sender is what
    // identifies the device, so it is in the entry that names it.
    public const int MaxMalformedReports = 3;

    // 3000 block: multicast DNS.

    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning, Message = "The multicast DNS browse found no usable network interface, so it reports no printers.")]
    public static partial void NoUsableInterface(ILogger logger);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "The responder at {RemoteEndPoint} sent a multicast DNS packet this browse cannot read; it was skipped.")]
    public static partial void MalformedMdnsPacket(ILogger logger, EndPoint remoteEndPoint, Exception exception);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "A multicast DNS browse channel failed; the {RecordCount} records that already arrived are kept.")]
    public static partial void BrowseChannelFailed(ILogger logger, int recordCount, Exception exception);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "The multicast DNS browse stopped at its limit of {MaxRecords} records, so some printers may be missing.")]
    public static partial void BrowseTruncated(ILogger logger, int maxRecords);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "The multicast DNS socket for interface address {Address} was refused, so that interface is not browsed.")]
    public static partial void InterfaceRefused(ILogger logger, IPAddress address, Exception exception);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning, Message = "{PacketCount} unreadable multicast DNS packets from {ResponderCount} responders were skipped in this browse.")]
    public static partial void MalformedMdnsPacketsSkipped(ILogger logger, int packetCount, int responderCount);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Debug, Message = "The multicast DNS browse read {RecordCount} records over {ChannelCount} channels and found {PrinterCount} printers.")]
    public static partial void BrowseCompleted(ILogger logger, int recordCount, int channelCount, int printerCount);

    // 3010 block: SNMP.

    [LoggerMessage(EventId = 3010, Level = LogLevel.Debug, Message = "SNMP attempt {Attempt} of {AttemptCount} to {Host}:{Port} failed.")]
    public static partial void SnmpAttemptFailed(ILogger logger, int attempt, int attemptCount, string host, int port, Exception exception);

    [LoggerMessage(EventId = 3011, Level = LogLevel.Warning, Message = "The SNMP agent at {Host} answered tooBig, so the supply walk asks for {MaxRepetitions} rows instead.")]
    public static partial void SnmpTooBig(ILogger logger, string host, int maxRepetitions);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning, Message = "The SNMP supply walk of {Host} stopped after {Rounds} rounds with {OpenColumns} columns open, so some supplies are missing.")]
    public static partial void SnmpWalkTruncated(ILogger logger, string host, int rounds, int openColumns);

    [LoggerMessage(EventId = 3013, Level = LogLevel.Warning, Message = "The SNMP agent at {RemoteEndPoint} sent a datagram this client cannot read; it was skipped.")]
    public static partial void MalformedSnmpDatagram(ILogger logger, EndPoint remoteEndPoint, Exception exception);

    [LoggerMessage(EventId = 3017, Level = LogLevel.Warning, Message = "{PacketCount} unreadable SNMP datagrams from {ResponderCount} agents were skipped.")]
    public static partial void MalformedSnmpDatagramsSkipped(ILogger logger, int packetCount, int responderCount);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Trace, Message = "An SNMP datagram from {RemoteEndPoint} carries request {ReplyId}, and {RequestId} was expected, so it was discarded.")]
    public static partial void StaleSnmpDatagram(ILogger logger, EndPoint remoteEndPoint, int replyId, int requestId);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Debug, Message = "SNMP request {RequestId} to {Host}:{Port} did not answer in time.")]
    public static partial void SnmpAttemptTimedOut(ILogger logger, int requestId, string host, int port);

    // 3020 block: the network probe.

    [LoggerMessage(EventId = 3020, Level = LogLevel.Trace, Message = "The probe of {Host}:{Port} was refused.")]
    public static partial void ProbeRefused(ILogger logger, string host, int port, Exception exception);

    [LoggerMessage(EventId = 3021, Level = LogLevel.Trace, Message = "The probe of {Host}:{Port} did not answer in time.")]
    public static partial void ProbeTimedOut(ILogger logger, string host, int port);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Debug, Message = "The network probe tried {HostCount} hosts on port {Port}, and {FoundCount} answered.")]
    public static partial void ProbeCompleted(ILogger logger, int hostCount, int port, int foundCount);

    // 3030 block: the queue correlator.

    [LoggerMessage(EventId = 3030, Level = LogLevel.Warning, Message = "Queue correlation was skipped: {CandidateCount} candidate channels is more than the limit of {MaxChannels}.")]
    public static partial void CorrelationOverLimit(ILogger logger, int candidateCount, int maxChannels);

    [LoggerMessage(EventId = 3031, Level = LogLevel.Debug, Message = "Queue correlation was skipped: {CandidateCount} candidate channels is fewer than two.")]
    public static partial void CorrelationTooFewCandidates(ILogger logger, int candidateCount);

    [LoggerMessage(EventId = 3032, Level = LogLevel.Debug, Message = "Queue correlation joined {LeftKey} and {RightKey}: their queues hold the same jobs.")]
    public static partial void JoinedByQueue(ILogger logger, PrinterDeviceKey leftKey, PrinterDeviceKey rightKey);

    [LoggerMessage(EventId = 3033, Level = LogLevel.Debug, Message = "Queue correlation joined {LeftKey} and {RightKey}: both queues hold tracer job {TracerName}.")]
    public static partial void JoinedByTracer(ILogger logger, PrinterDeviceKey leftKey, PrinterDeviceKey rightKey, string tracerName);

    [LoggerMessage(EventId = 3034, Level = LogLevel.Information, Message = "A tracer job named {TracerName} was written to printer {PrinterId} to correlate its queue.")]
    public static partial void TracerJobWritten(ILogger logger, string tracerName, PrinterId printerId);

    [LoggerMessage(EventId = 3035, Level = LogLevel.Error, Message = "Tracer job {JobId} on printer {PrinterId} was not cancelled and may stay in that queue.")]
    public static partial void TracerJobNotCancelled(ILogger logger, string jobId, PrinterId printerId, Exception exception);

    [LoggerMessage(EventId = 3036, Level = LogLevel.Warning, Message = "The queue correlation step {Step} on printer {PrinterId} failed, so that channel gives no evidence.")]
    public static partial void CorrelationStepFailed(ILogger logger, string step, PrinterId printerId, Exception exception);

    [LoggerMessage(EventId = 3037, Level = LogLevel.Debug, Message = "Queue correlation read {CandidateCount} channels and proved {GroupCount} shared queues.")]
    public static partial void CorrelationCompleted(ILogger logger, int candidateCount, int groupCount);
}
