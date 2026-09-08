using System.Text;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Raw byte stream sent to a printer, annotated with its content type
/// so transports and spoolers can route it correctly.
/// </summary>
public sealed class PrinterPayload
{
    private PrinterPayload(ReadOnlyMemory<byte> data, string contentType)
    {
        Data = data;
        ContentType = contentType;
    }

    /// <summary>
    /// Gets the raw bytes to transmit.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// Gets the content type of the payload. See <see cref="PrinterContentTypes"/>.
    /// </summary>
    public string ContentType { get; }

    /// <summary>
    /// Creates a payload from raw bytes.
    /// </summary>
    /// <param name="data">The raw bytes to transmit. The payload references the buffer; callers must not mutate it afterwards.</param>
    /// <param name="contentType">The content type of the payload. See <see cref="PrinterContentTypes"/>.</param>
    /// <returns>The payload.</returns>
    public static PrinterPayload FromBytes(ReadOnlyMemory<byte> data, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        return new PrinterPayload(data, contentType);
    }

    /// <summary>
    /// Creates a payload by encoding text, for text-based languages such as ZPL or EPL.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="contentType">The content type of the payload. See <see cref="PrinterContentTypes"/>.</param>
    /// <param name="encoding">The encoding to use. Defaults to UTF-8 when not specified.</param>
    /// <returns>The payload.</returns>
    public static PrinterPayload FromString(string text, string contentType, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        encoding ??= Encoding.UTF8;
        return new PrinterPayload(encoding.GetBytes(text), contentType);
    }
}
