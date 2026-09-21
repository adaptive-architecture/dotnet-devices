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

    /// <summary>
    /// Gets the width of the media each page is printed on, in pixels at <see cref="Dpi"/>,
    /// or <c>null</c> when the channel does not know it while converting.
    /// </summary>
    /// <remarks>
    /// A converter that has this can lay the page out itself and hand back a page the size
    /// of the media, which is the only way a placement reaches a printer that applies none.
    /// A converter that ignores it returns pages at their own size, which is what every
    /// converter written before this member did, and which stays correct.
    /// <para>
    /// The Windows spooler converts before it builds the device mode, so it knows no media
    /// here and says so with <c>null</c>; it places the page at the draw step instead.
    /// </para>
    /// </remarks>
    public int? MediaWidthPixels { get; init; }

    /// <summary>
    /// Gets the height of the media each page is printed on, in pixels at <see cref="Dpi"/>,
    /// or <c>null</c> when the channel does not know it while converting.
    /// </summary>
    public int? MediaHeightPixels { get; init; }

    /// <summary>
    /// Gets the name of that media, such as <c>na_letter_8.5x11in</c>, or <c>null</c> when
    /// the job named none. A raster carries it in each page header.
    /// </summary>
    public string? MediaName { get; init; }

    /// <summary>
    /// Gets how the page is fitted to the media, or <c>null</c> when the job named nothing.
    /// Only a converter that knows the media can apply it.
    /// </summary>
    public PrintScaling? Scaling { get; init; }

    /// <summary>
    /// Gets the orientation of the job, or <c>null</c> when it named none.
    /// </summary>
    /// <remarks>
    /// A converter does not turn the pixels: the printer applies the orientation itself, and
    /// turning them here would apply it twice. It is carried so that a converter can judge
    /// the media it is fitting to.
    /// </remarks>
    public PrintOrientation? Orientation { get; init; }

    /// <summary>
    /// Gets where the page lands on the media, or <c>null</c> for the centred placement.
    /// </summary>
    public PrintPlacement? Placement { get; init; }

    /// <summary>
    /// Gets whether the renderer smooths what it draws, or <c>null</c> for the engine
    /// default.
    /// </summary>
    public bool? Smoothing { get; init; }

    /// <summary>
    /// Gets what decides the media size. <see cref="MediaSizeSource.Document"/> asks the
    /// converter to make the media the size of the page itself, whatever
    /// <see cref="MediaWidthPixels"/> says.
    /// </summary>
    public MediaSizeSource MediaSizeSource { get; init; }
}
