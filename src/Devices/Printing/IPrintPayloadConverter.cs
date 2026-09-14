namespace AdaptArch.Devices.Printing;

/// <summary>
/// Turns a format a channel cannot print into pages it can.
/// </summary>
/// <remarks>
/// Register an implementation through <see cref="PrinterManagerOptions.Converters"/>, or
/// through <see cref="PrintFormatPolicy.AddDefaultConverter"/> for an application that
/// builds no manager. A converter runs before the job reaches the spooler, so a file it
/// refuses spools nothing.
/// </remarks>
public interface IPrintPayloadConverter
{
    /// <summary>
    /// Tells whether this converter reads the content type.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns><c>true</c> when <see cref="ConvertAsync"/> may be called for it.</returns>
    bool CanConvert(string contentType);

    /// <summary>
    /// Converts the payload into one image per page, in document order.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="context">The resolution, the pages and the target format asked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One image per page. An empty list is not a valid answer.</returns>
    Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken);
}
