using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing;

// The log of the printer manager, the routing and the job path. The [LoggerMessage] source
// generator writes the bodies, so nothing here uses reflection and the package stays
// trim-safe and AOT-safe.
//
// An event identifier is a promise. It is never reused for a different meaning and never
// renumbered, and a retired event leaves a hole. The words of a message may be improved; the
// identifier and what it reports may not. That is what lets a report say "send us every line
// with event 2003" and still work against a later version.
//
// Blocks: 2000 discovery, 2010 enrichment, 2020 correlation, 2030 routing,
// 2040 formats and options, 2050 job milestones, 2060 the job monitor, 2070 raw transport.
internal static partial class PrintingLog
{
    public const string Category = "AdaptArch.Devices.Printing";

    public static ILogger Create(ILoggerFactory? factory) => DeviceLog.Create(factory, Category);

    // 2000 block: discovery.

    [LoggerMessage(EventId = 2000, Level = LogLevel.Error, Message = "Discovery source {Source} failed and was skipped; {AnsweredCount} of {SourceCount} sources answered.")]
    public static partial void DiscoverySourceFailed(ILogger logger, DiscoverySource source, int answeredCount, int sourceCount, Exception exception);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Discovery source {Source} failed; every source failed, so the caller gets the failure of each one.")]
    public static partial void EverySourceFailed(ILogger logger, DiscoverySource source, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Discovery found {FoundCount} channels and kept {KeptCount} after it removed the repeats.")]
    public static partial void ChannelsDeduplicated(ILogger logger, int foundCount, int keptCount);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Discovery found {DeviceCount} printers over {ChannelCount} channels in {ElapsedMs} ms.")]
    public static partial void DiscoveryCompleted(ILogger logger, int deviceCount, int channelCount, long elapsedMs);

    // 2010 block: enrichment.

    [LoggerMessage(EventId = 2010, Level = LogLevel.Warning, Message = "The capability read of printer {PrinterId} at {Endpoint} failed; its configuration stays unread.")]
    public static partial void CapabilityReadFailed(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint, Exception exception);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Warning, Message = "The identity read of printer {PrinterId} at {Endpoint} failed; it may not group with the other channels of its device.")]
    public static partial void IdentityReadFailed(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint, Exception exception);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Warning, Message = "Enrichment could not open channel {PrinterId} at {Endpoint}; it stays as discovery found it.")]
    public static partial void EnrichmentOpenFailed(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint, Exception exception);

    // 2020 block: correlation.

    [LoggerMessage(EventId = 2020, Level = LogLevel.Warning, Message = "Correlation could not open channel {PrinterId} at {Endpoint}; that channel gives no evidence.")]
    public static partial void CorrelationOpenFailed(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint, Exception exception);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Error, Message = "Queue correlation failed; {ChannelCount} channels stay separate and one printer may show as more than one.")]
    public static partial void CorrelationFailed(ILogger logger, int channelCount, Exception exception);

    // 2030 block: routing.

    [LoggerMessage(EventId = 2030, Level = LogLevel.Debug, Message = "Status channel {Endpoint} of printer {PrinterId} did not answer.")]
    public static partial void StatusChannelFailed(ILogger logger, PrinterEndpoint endpoint, PrinterId printerId, Exception exception);

    [LoggerMessage(EventId = 2031, Level = LogLevel.Warning, Message = "The status of printer {PrinterId} came from {Endpoint} after {FailedCount} channels did not answer.")]
    public static partial void StatusCameFromFallback(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint, int failedCount);

    [LoggerMessage(EventId = 2032, Level = LogLevel.Warning, Message = "The job of {ContentType} for printer {PrinterId} goes to {Endpoint}, which gives no passthrough; the printer may print the bytes as text.")]
    public static partial void PrintChannelGivesNoPassthrough(ILogger logger, string contentType, PrinterId printerId, PrinterEndpoint endpoint);

    [LoggerMessage(EventId = 2033, Level = LogLevel.Debug, Message = "The job of {ContentType} for printer {PrinterId} goes to {Endpoint}, out of {CandidateCount} channels; passthrough wanted: {WantsPassthrough}.")]
    public static partial void PrintChannelChosen(ILogger logger, string contentType, PrinterId printerId, PrinterEndpoint endpoint, int candidateCount, bool wantsPassthrough);

    [LoggerMessage(EventId = 2034, Level = LogLevel.Warning, Message = "The queue read of printer {PrinterId} goes to {Endpoint}, which has no job queue.")]
    public static partial void QueueChannelHasNoQueue(ILogger logger, PrinterId printerId, PrinterEndpoint endpoint);

    [LoggerMessage(EventId = 2035, Level = LogLevel.Debug, Message = "Printer {PrinterId} was not known, and its identifier names an address, so it was opened with no discovery.")]
    public static partial void PrinterOpenedFromAddress(ILogger logger, PrinterId printerId);

    [LoggerMessage(EventId = 2036, Level = LogLevel.Information, Message = "Printer {PrinterId} was not known and names no address, so a discovery ran to find it.")]
    public static partial void DiscoveryRanToResolve(ILogger logger, PrinterId printerId);

    // 2040 block: formats and options.

    [LoggerMessage(EventId = 2040, Level = LogLevel.Warning, Message = "Printer {PrinterId} dropped the options {Dropped} from job {JobId}, so the job prints with the settings of the queue instead.")]
    public static partial void OptionsDropped(ILogger logger, PrinterId printerId, string dropped, string jobId);

    // 2050 block: job milestones. A job name is personal data, so it is at Debug only.

    [LoggerMessage(EventId = 2050, Level = LogLevel.Information, Message = "Job {JobId} for printer {PrinterId} was submitted on {Endpoint} as {ContentType}, {ByteCount} bytes.")]
    public static partial void JobSubmitted(ILogger logger, string jobId, PrinterId printerId, PrinterEndpoint endpoint, string contentType, int byteCount);

    [LoggerMessage(EventId = 2051, Level = LogLevel.Debug, Message = "Job {JobId} for printer {PrinterId} is named {JobName} and was sent by {UserName}.")]
    public static partial void JobNamed(ILogger logger, string jobId, PrinterId printerId, string? jobName, string? userName);

    // 2060 block: the job monitor.

    [LoggerMessage(EventId = 2060, Level = LogLevel.Information, Message = "Job {JobId} on printer {PrinterId} reached {State}.")]
    public static partial void JobReachedTerminalState(ILogger logger, string jobId, PrinterId printerId, PrintJobState state);

    [LoggerMessage(EventId = 2061, Level = LogLevel.Information, Message = "Job {JobId} on printer {PrinterId} left the queue after it was seen in {LastState}, so it is reported complete.")]
    public static partial void JobLeftTheQueue(ILogger logger, string jobId, PrinterId printerId, PrintJobState lastState);

    [LoggerMessage(EventId = 2062, Level = LogLevel.Warning, Message = "Job {JobId} on printer {PrinterId} left the queue before it was seen to print. It is reported complete, so a cancel or a purge looks the same as a success.")]
    public static partial void JobVanishedBeforeItPrinted(ILogger logger, string jobId, PrinterId printerId);

    [LoggerMessage(EventId = 2063, Level = LogLevel.Debug, Message = "The watch of job {JobId} on printer {PrinterId} ended: {Reason}, after {ReadingCount} readings.")]
    public static partial void WatchEnded(ILogger logger, string jobId, PrinterId printerId, string reason, int readingCount);

    [LoggerMessage(EventId = 2064, Level = LogLevel.Trace, Message = "Job {JobId} on printer {PrinterId} reads {State}, {Impressions} of {TotalImpressions} impressions.")]
    public static partial void JobRead(ILogger logger, string jobId, PrinterId printerId, PrintJobState state, int? impressions, int? totalImpressions);

    // 2070 block: the raw transport.

    [LoggerMessage(EventId = 2070, Level = LogLevel.Error, Message = "The close of the raw channel to {Host}:{Port} failed after {ByteCount} bytes. Some printers print the last page only after this, so the job may be incomplete.")]
    public static partial void RawShutdownFailed(ILogger logger, string host, int port, int byteCount, Exception exception);

    [LoggerMessage(EventId = 2071, Level = LogLevel.Information, Message = "A raw job wrote {ByteCount} bytes to {Host}:{Port}. A raw channel has no queue, so nothing else reports what the printer did with it.")]
    public static partial void RawJobWritten(ILogger logger, int byteCount, string host, int port);

    [LoggerMessage(EventId = 2072, Level = LogLevel.Debug, Message = "SNMP did not answer for printer {PrinterId} at {Host}, so IPP is tried next.")]
    public static partial void SnmpStatusFailed(ILogger logger, PrinterId printerId, string host, Exception exception);

    [LoggerMessage(EventId = 2073, Level = LogLevel.Error, Message = "Neither SNMP nor IPP answered for printer {PrinterId} at {Host}, so its state is reported as Unknown.")]
    public static partial void NoStatusSource(ILogger logger, PrinterId printerId, string host, Exception exception);
}
