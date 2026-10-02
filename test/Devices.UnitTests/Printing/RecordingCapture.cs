using AdaptArch.Devices.Printing;

namespace AdaptArch.Devices.UnitTests.Printing;

// Keeps every write a channel handed over, with the bytes copied: the memory a capture is
// given is valid for the call only.
internal sealed class RecordingCapture : IPrintCapture
{
    public List<(PrinterId PrinterId, PrinterEndpoint Endpoint, string ContentType, byte[] Data, string ConverterUsed)> Writes { get; } = [];

    public ValueTask CaptureAsync(PrintCapture capture, CancellationToken cancellationToken)
    {
        Writes.Add((capture.PrinterId, capture.Endpoint, capture.ContentType, capture.Data.ToArray(), capture.ConverterUsed));
        return ValueTask.CompletedTask;
    }
}
