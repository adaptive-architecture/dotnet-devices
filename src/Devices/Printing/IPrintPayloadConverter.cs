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
    /// Gets the name a job uses to ask for this converter rather than another.
    /// </summary>
    /// <remarks>
    /// Only needed where more than one converter reads a format, which is what
    /// <see cref="PrintOptions.ConverterName"/> chooses between: PDF is read by both
    /// <c>AdaptArch.Devices.Pdfium</c> and <c>AdaptArch.Devices.Windows</c>. The default is
    /// the type name, so a converter nobody chooses between needs to do nothing and every
    /// converter written before this member still compiles.
    /// <para>
    /// Names are matched case-insensitively, and should be short, stable and readable: an
    /// application puts them in front of a person. Elsewhere this library names a strategy
    /// with an enumeration, as <see cref="DiscoverySource"/> and <see cref="PrinterScheme"/>
    /// do, but no enumeration can carry a converter an application wrote, so this one is text.
    /// </para>
    /// </remarks>
    string Name => GetType().Name;

    /// <summary>
    /// Tells whether this converter reads the content type.
    /// </summary>
    /// <param name="contentType">The media type of the payload.</param>
    /// <returns><c>true</c> when <see cref="ConvertAsync"/> may be called for it.</returns>
    bool CanConvert(string contentType);

    /// <summary>
    /// Tells whether this converter can produce the target content type.
    /// </summary>
    /// <param name="targetContentType">The media type asked for in <see cref="PrintConversionContext.TargetContentType"/>.</param>
    /// <returns><c>true</c> when <see cref="ConvertAsync"/> may be asked for it.</returns>
    /// <remarks>
    /// The default answers for <see cref="PrinterContentTypes.Png"/> only, which is what the
    /// Windows spooler asks for and what every converter written before this member existed
    /// produces. Override it to reach a channel that reads something else: an IPP printer
    /// reads <see cref="PrinterContentTypes.PwgRaster"/> and never PNG. A converter that
    /// answers for more than one target must honour
    /// <see cref="PrintConversionContext.TargetContentType"/> rather than picking for itself.
    /// </remarks>
    bool CanEmit(string targetContentType) =>
        String.Equals(targetContentType, PrinterContentTypes.Png, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts the payload into one image per page, in document order.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="context">The resolution, the pages and the target format asked for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One image per page. An empty list is not a valid answer.</returns>
    Task<IReadOnlyList<byte[]>> ConvertAsync(byte[] data, PrintConversionContext context, CancellationToken cancellationToken);
}
