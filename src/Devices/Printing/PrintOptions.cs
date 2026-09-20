namespace AdaptArch.Devices.Printing;

/// <summary>
/// Per-job printing options. Every property is optional; unset properties
/// fall back to the printer or driver default, which keeps the options
/// portable across spoolers with different capabilities.
/// </summary>
/// <remarks>
/// On the Windows print spooler, <see cref="JobName"/>, <see cref="Duplex"/>,
/// <see cref="ColorMode"/>, <see cref="Orientation"/>, <see cref="MediaSource"/>,
/// <see cref="MediaSize"/>, <see cref="ResolutionDpi"/> and <see cref="Quality"/> travel
/// in a device mode the print driver builds. Printer languages use the <c>RAW</c> data
/// type, which never reads the copy count, so <see cref="Copies"/> is printed as one
/// document for each copy there. PNG and JPEG images are drawn onto a GDI printer
/// device context instead, which honours <see cref="Copies"/> as <c>dmCopies</c> in one
/// job and applies <see cref="Orientation"/> and <see cref="Scaling"/> when the image
/// is laid out, including the reversed orientations and the fit modes that have no
/// device mode field. <see cref="MediaType"/>, <see cref="OutputBin"/>, <see cref="PageRanges"/>
/// and <see cref="NumberUp"/> have no device mode field, so they are reported in
/// <see cref="PrintJobInfo.DroppedOptions"/>.
/// </remarks>
public sealed class PrintOptions
{
    /// <summary>
    /// The user name an IPP request carries when <see cref="RequestingUserName"/> is not set.
    /// </summary>
    public const string DefaultRequestingUserName = "anonymous";

    private int? _copies;
    private int? _numberUp;

    /// <summary>
    /// Gets or sets the number of copies. Must be positive when set.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is zero or negative.</exception>
    public int? Copies
    {
        get => _copies;
        set
        {
            if (value is int copies)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(copies, 1);
            }

            _copies = value;
        }
    }

    /// <summary>
    /// Gets or sets the duplex mode.
    /// </summary>
    public DuplexMode? Duplex { get; set; }

    /// <summary>
    /// Gets or sets the color mode.
    /// </summary>
    public PrintColorMode? ColorMode { get; set; }

    /// <summary>
    /// Gets or sets the page orientation, which is also the rotation on the media.
    /// </summary>
    public PrintOrientation? Orientation { get; set; }

    /// <summary>
    /// Gets or sets how the document is fitted to the media.
    /// </summary>
    public PrintScaling? Scaling { get; set; }

    /// <summary>
    /// Gets or sets the media source (tray) name.
    /// </summary>
    public string? MediaSource { get; set; }

    /// <summary>
    /// Gets or sets the media (paper or label) size name.
    /// </summary>
    public string? MediaSize { get; set; }

    /// <summary>
    /// Gets or sets the media type name, such as <c>stationery</c> or <c>labels</c>.
    /// </summary>
    /// <remarks>
    /// IPP carries the media type inside <c>media-col</c>, which must not be sent
    /// together with <c>media</c>. When this property and <see cref="MediaSize"/> are
    /// both set, the library puts both into <c>media-col</c>.
    /// </remarks>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets the output bin the printer delivers the job to.
    /// </summary>
    public string? OutputBin { get; set; }

    /// <summary>
    /// Gets or sets the print resolution in dots per inch.
    /// </summary>
    public int? ResolutionDpi { get; set; }

    /// <summary>
    /// Gets or sets the print quality.
    /// </summary>
    public PrintQuality? Quality { get; set; }

    /// <summary>
    /// Gets or sets the pages to print. An unset value prints the whole document.
    /// </summary>
    public IReadOnlyList<PageRange>? PageRanges { get; set; }

    /// <summary>
    /// Gets or sets the name of the converter that renders this job, when more than one
    /// reads its format.
    /// </summary>
    /// <remarks>
    /// Unlike every other property here, this one is not a printer setting and never leaves
    /// the library: it picks an <see cref="IPrintPayloadConverter"/> by its
    /// <see cref="IPrintPayloadConverter.Name"/>, matched case-insensitively. An unset value
    /// takes the converter the policy prefers, which is the first one registered for the
    /// format.
    /// <para>
    /// Setting it also says the document itself is not what should be sent. An IPP printer
    /// that reads the payload as it is normally receives it untouched, because its own
    /// interpreter beats a raster of ours and the job is a fraction of the size; a job that
    /// names a converter is converted anyway. Nobody names an engine as a preference, so
    /// naming one is taken as asking for it to run. Where the printer reads nothing that
    /// converter writes the document is still sent as it is, since a job that would have
    /// printed correctly should not fail, and the reason is logged.
    /// </para>
    /// <para>
    /// A name no registered converter carries fails the job with
    /// <see cref="NotSupportedException"/> rather than quietly rendering with another engine,
    /// because a job that named one asked for that one.
    /// <see cref="PrintFormatPolicy.ConvertersFor"/> lists what a process can be asked for.
    /// </para>
    /// </remarks>
    public string? ConverterName { get; set; }

    /// <summary>
    /// Gets or sets the number of pages to put on one sheet. Must be positive when set.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is zero or negative.</exception>
    public int? NumberUp
    {
        get => _numberUp;
        set
        {
            if (value is int pages)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
            }

            _numberUp = value;
        }
    }

    /// <summary>
    /// Gets or sets the human-readable job name shown in print queues.
    /// </summary>
    public string? JobName { get; set; }

    /// <summary>
    /// Gets or sets the user name an IPP request carries as <c>requesting-user-name</c>.
    /// RFC 8011 says a client should send it, and CUPS uses it for its owner-based cancel
    /// policy. Defaults to <see cref="DefaultRequestingUserName"/> when not set. The
    /// operating system spooler and the raw channel do not use it.
    /// </summary>
    public string? RequestingUserName { get; set; }

    /// <summary>
    /// Gets or sets what to do with an option the printer does not support.
    /// A printer that reports no configuration cannot say which options it supports,
    /// so <see cref="UnsupportedOptionBehavior.Throw"/> and
    /// <see cref="UnsupportedOptionBehavior.Drop"/> then act as
    /// <see cref="UnsupportedOptionBehavior.Send"/>.
    /// </summary>
    public UnsupportedOptionBehavior OnUnsupported { get; set; } = UnsupportedOptionBehavior.Send;
}
