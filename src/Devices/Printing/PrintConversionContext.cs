namespace AdaptArch.Devices.Printing;

/// <summary>
/// What a converter is asked to produce.
/// </summary>
/// <param name="ContentType">The media type of the bytes given to the converter.</param>
/// <param name="TargetContentType">The media type of each page the converter returns. Today this is always <see cref="PrinterContentTypes.Png"/>.</param>
/// <param name="Dpi">The resolution the caller asked for, or 300 when the job named none. A converter clamps it to what its engine renders well.</param>
/// <param name="PageRanges">The 1-based pages the caller asked for, or <c>null</c> for the whole document.</param>
/// <param name="QueueName">The channel the pages print on, for a message that names it.</param>
public sealed record PrintConversionContext(
    string ContentType,
    string TargetContentType,
    int Dpi,
    IReadOnlyList<PageRange>? PageRanges,
    string QueueName)
{
    /// <summary>
    /// The resolution a document is converted at when the job names none.
    /// </summary>
    /// <remarks>
    /// Nothing clamps this: a limit of one engine must not quietly reduce the request given
    /// to another, so each converter clamps to what it renders well.
    /// </remarks>
    public const int DefaultDpi = 300;
}
