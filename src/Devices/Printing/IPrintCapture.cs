namespace AdaptArch.Devices.Printing;

/// <summary>
/// Receives the bytes a channel sends to a printer, just before they leave the process, so a
/// job that prints wrong can be read as the printer received it.
/// </summary>
/// <remarks>
/// Set it on <see cref="PrinterFactory.Capture"/>, or register it with the container, and every
/// printer the factory opens calls it once for each write: the raw channel with what it writes
/// to the socket, an IPP printer or a CUPS queue with the document as submitted, converted
/// where the library converted it, a Windows queue with the bytes it spools as RAW, and the GDI
/// image path with the PNG of each page it draws. A document drawn as vectors on Windows is
/// GDI drawing calls and carries no bytes, so nothing is captured for it. The library awaits
/// the call, so a capture that writes a file is safe and a slow one slows the job.
/// </remarks>
public interface IPrintCapture
{
    /// <summary>
    /// Receives one write.
    /// </summary>
    /// <param name="capture">What is sent, and to which printer.</param>
    /// <param name="cancellationToken">The cancellation token of the print.</param>
    /// <returns>A task that completes when the capture is recorded.</returns>
    ValueTask CaptureAsync(PrintCapture capture, CancellationToken cancellationToken);
}

/// <summary>
/// One write a channel made: the bytes, their content type, and the printer they went to.
/// </summary>
/// <param name="PrinterId">The printer the bytes were sent to.</param>
/// <param name="Endpoint">The endpoint they were written to.</param>
/// <param name="ContentType">The content type of the bytes as sent, which is the converted format where the library converted the document.</param>
/// <param name="Data">The bytes as sent. The memory is valid for the call only; copy it to keep it.</param>
/// <param name="ConverterUsed">The name of the converter that rendered the bytes, or <c>null</c> when the document went as it was.</param>
public sealed record PrintCapture(
    PrinterId PrinterId,
    PrinterEndpoint Endpoint,
    string ContentType,
    ReadOnlyMemory<byte> Data,
    string? ConverterUsed = null);
