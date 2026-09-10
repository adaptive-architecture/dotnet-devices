namespace AdaptArch.Devices.Printing;

/// <summary>
/// Capabilities and configuration of a printer.
/// </summary>
public sealed class PrinterConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterConfiguration"/> class.
    /// </summary>
    /// <param name="printerId">The printer identifier.</param>
    public PrinterConfiguration(PrinterId printerId)
    {
        PrinterId = printerId;
        SupportedResolutionsDpi = [];
        MediaSizes = [];
        Media = [];
        MediaSources = [];
        MediaTypes = [];
        OutputBins = [];
        Qualities = [];
        NumberUpValues = [];
        SupportedDocumentFormats = [];
        SupportedOrientations = [];
        SupportedScalings = [];
    }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId PrinterId { get; }

    /// <summary>
    /// Gets the supported print resolutions in dots per inch.
    /// </summary>
    public IReadOnlyList<int> SupportedResolutionsDpi { get; init; }

    /// <summary>
    /// Gets a value indicating whether duplex printing is supported. <c>null</c> means
    /// the printer did not report it, which is not the same as <c>false</c>.
    /// </summary>
    public bool? SupportsDuplex { get; init; }

    /// <summary>
    /// Gets a value indicating whether color printing is supported. <c>null</c> means
    /// the printer did not report it, which is not the same as <c>false</c>.
    /// </summary>
    public bool? SupportsColor { get; init; }

    /// <summary>
    /// Gets the page orientations the printer accepts. An empty list means the printer
    /// did not report them, which is not the same as "accepts none".
    /// </summary>
    public IReadOnlyList<PrintOrientation> SupportedOrientations { get; init; }

    /// <summary>
    /// Gets the scaling modes the printer accepts. An empty list means the printer did
    /// not report them, which is not the same as "accepts none".
    /// </summary>
    public IReadOnlyList<PrintScaling> SupportedScalings { get; init; }

    /// <summary>
    /// Gets the supported media (paper or label) size names.
    /// </summary>
    public IReadOnlyList<string> MediaSizes { get; init; }

    /// <summary>
    /// Gets the same media sizes as <see cref="MediaSizes"/>, in the same order, with
    /// the number the Windows device mode uses beside each name. The two lists are
    /// always filled together.
    /// </summary>
    /// <remarks>
    /// Only the Windows spooler reports a number. On every other channel each entry
    /// carries the name and a <c>null</c> number.
    /// </remarks>
    public IReadOnlyList<PrinterMedia> Media { get; init; }

    /// <summary>
    /// Gets the media sources (trays) the printer can take paper from. An empty list
    /// means the printer did not report them, which is not the same as "has none".
    /// </summary>
    public IReadOnlyList<PrinterMediaSource> MediaSources { get; init; }

    /// <summary>
    /// Gets the media types the printer accepts, such as <c>stationery</c> or
    /// <c>labels</c>. An empty list means the printer did not report them.
    /// </summary>
    public IReadOnlyList<string> MediaTypes { get; init; }

    /// <summary>
    /// Gets the output bins the printer can deliver a job to. An empty list means the
    /// printer did not report them.
    /// </summary>
    public IReadOnlyList<string> OutputBins { get; init; }

    /// <summary>
    /// Gets the print qualities the printer accepts. An empty list means the printer
    /// did not report them.
    /// </summary>
    public IReadOnlyList<PrintQuality> Qualities { get; init; }

    /// <summary>
    /// Gets the number of pages the printer can put on one sheet. An empty list means
    /// the printer did not report them.
    /// </summary>
    public IReadOnlyList<int> NumberUpValues { get; init; }

    /// <summary>
    /// Gets a value indicating whether the printer accepts a page range. <c>null</c>
    /// means the printer did not report it, which is not the same as <c>false</c>.
    /// </summary>
    public bool? SupportsPageRanges { get; init; }

    /// <summary>
    /// Gets the default media size name, when known.
    /// </summary>
    public string? DefaultMediaSize { get; init; }

    /// <summary>
    /// Gets the name of the tray the printer uses when a job names none, when known.
    /// </summary>
    public string? DefaultMediaSource { get; init; }

    /// <summary>
    /// Gets the orientation the printer uses when a job names none, when known.
    /// </summary>
    public PrintOrientation? DefaultOrientation { get; init; }

    /// <summary>
    /// Gets the resolution in dots per inch the printer uses when a job names none,
    /// when known.
    /// </summary>
    public int? DefaultResolutionDpi { get; init; }

    /// <summary>
    /// Gets the document formats (MIME media types) the printer accepts. An empty list
    /// means the printer did not report them, which is not the same as "accepts none".
    /// </summary>
    public IReadOnlyList<string> SupportedDocumentFormats { get; init; }
}
