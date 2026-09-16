using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Ipp;

// The log of the IPP path. Read the note on event identifiers in PrintingLog.
//
// An operation that fails and reaches the caller as an exception stays at Debug: the
// exception already carries it, and a second report of the same failure helps nobody.
//
// Blocks: 1000 the endpoint probe, 1010 the operations, 1020 the jobs, 1030 the format.
internal static partial class IppLog
{
    public const string Category = "AdaptArch.Devices.Printing.Ipp";

    public static ILogger Create(ILoggerFactory? factory) => DeviceLog.Create(factory, Category);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "IPP probe of {Endpoint} answered.")]
    public static partial void EndpointProbed(ILogger logger, Uri endpoint);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "IPP probe of {Endpoint} did not answer.")]
    public static partial void EndpointProbeFailed(ILogger logger, Uri endpoint, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "IPP endpoint {Endpoint} serves printer {Host}:{Port}.")]
    public static partial void EndpointResolved(ILogger logger, Uri endpoint, string host, int port);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Debug, Message = "IPP {Operation} to {Endpoint} returned IPP status {Status}.")]
    public static partial void OperationCompleted(ILogger logger, string operation, Uri endpoint, int? status);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Debug, Message = "IPP {Operation} to {Endpoint} failed with IPP status {Status}.")]
    public static partial void OperationFailed(ILogger logger, string operation, Uri endpoint, int? status, Exception exception);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Debug, Message = "The job of {ContentType} for {Endpoint} is sent with the document format {Format}.")]
    public static partial void DocumentFormatChosen(ILogger logger, string contentType, Uri endpoint, string format);

    [LoggerMessage(EventId = 1031, Level = LogLevel.Warning, Message = "Printer {Endpoint} lists neither {ContentType} nor application/vnd.cups-raw, so the job is sent as {Format}. A printer language sent this way may print as text instead of as a label.")]
    public static partial void DocumentFormatDowngraded(ILogger logger, Uri endpoint, string contentType, string format);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Debug, Message = "IPP job {JobId} on {Endpoint} is {State}; reasons {Reasons}; message {Message}.")]
    public static partial void JobRead(ILogger logger, string jobId, Uri endpoint, string state, string? reasons, string? message);
}
