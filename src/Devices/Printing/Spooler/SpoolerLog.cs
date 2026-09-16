using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Spooler;

// The log of the Windows spooler. Read the note on event identifiers in PrintingLog.
//
// The CUPS spooler is not here: it speaks IPP, so it writes the events of IppLog.
//
// Blocks: 4000 the driver, 4010 the operating system interop, 4020 the device mode.
internal static partial class SpoolerLog
{
    public const string Category = "AdaptArch.Devices.Printing.Spooler";

    public static ILogger Create(ILoggerFactory? factory) => DeviceLog.Create(factory, Category);

    // 4000 block: the driver.

    [LoggerMessage(EventId = 4000, Level = LogLevel.Debug, Message = "The Windows spooler queue {QueueName} took job {JobId} of {ByteCount} bytes as {ContentType}.")]
    public static partial void JobSpooled(ILogger logger, string queueName, int jobId, int byteCount, string contentType);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "The Windows spooler reported {QueueCount} queues.")]
    public static partial void QueuesEnumerated(ILogger logger, int queueCount);

    // 4010 block: the operating system interop.

    // 4010 is not used. An operating system failure reaches the caller as an exception that
    // already names the operation and the message, so the library does not report it twice.
    // The identifier is kept free, because an identifier is never reused for another meaning.

    [LoggerMessage(EventId = 4011, Level = LogLevel.Error, Message = "The identity of the Windows queue {QueueName} was not read, so its port aliases are lost and it may show as a printer of its own.")]
    public static partial void QueueIdentityNotRead(ILogger logger, string queueName, Exception exception);

    // 4020 block: the device mode.

    [LoggerMessage(EventId = 4020, Level = LogLevel.Warning, Message = "The driver of queue {QueueName} did not apply the options {Options}. Windows carries no media type, output bin, page range or pages per sheet in a device mode.")]
    public static partial void DeviceModeOptionsDropped(ILogger logger, string queueName, string options);
}
