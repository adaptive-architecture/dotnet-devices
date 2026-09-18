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

    /// <summary>
    /// Gets the raster colour space the printer asked for, such as <c>srgb_8</c>, or
    /// <c>null</c> when it named none or the target is not a raster.
    /// </summary>
    /// <remarks>
    /// Chosen from <see cref="PrinterConfiguration.PwgRasterTypes"/> and the colour mode of
    /// the job. A converter that cannot produce it should produce what it can rather than
    /// fail: the printer is the one that judges the result.
    /// </remarks>
    public string? RasterType { get; init; }

    /// <summary>
    /// Gets how the printer reads the back of a duplex sheet, or <c>null</c> when it named
    /// nothing or the job is one-sided.
    /// </summary>
    /// <remarks>
    /// Carries <see cref="PrinterConfiguration.PwgRasterSheetBack"/> unchanged. A converter
    /// that ignores it on a duplex job writes every second page upside down or mirrored,
    /// and nothing reports that as an error.
    /// </remarks>
    public string? SheetBack { get; init; }

    /// <summary>
    /// Gets the duplex mode of the job, or <c>null</c> when it named none.
    /// </summary>
    /// <remarks>
    /// A raster carries the duplex mode in each page header, so the converter needs it even
    /// though the job template carries it too.
    /// </remarks>
    public DuplexMode? Duplex { get; init; }
}
