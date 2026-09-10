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
/// in a device mode the print driver builds, and <see cref="Copies"/> is printed as one
/// document for each copy, because a queue with the <c>RAW</c> data type does not read the
/// copy count. <see cref="MediaType"/>, <see cref="OutputBin"/>, <see cref="PageRanges"/>
/// and <see cref="NumberUp"/> have no device mode field, so they are reported in
/// <see cref="PrintJobInfo.DroppedOptions"/>. <see cref="Scaling"/> reaches the device
/// mode scale field only as <see cref="PrintScaling.None"/>, which is 100 per cent, and an
/// <see cref="Orientation"/> of <see cref="PrintOrientation.ReverseLandscape"/> or
/// <see cref="PrintOrientation.ReversePortrait"/> has no device mode value, so both are
/// dropped the same way. A <see cref="MediaSize"/> or
/// <see cref="MediaSource"/> name that the queue did not report has no device mode number
/// either, and is dropped the same way. IPP printers and the CUPS spooler driver on Linux
/// and macOS honour every property.
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

    /// <summary>
    /// Gets or sets a value that says the device must receive the payload bytes unchanged.
    /// </summary>
    /// <remarks>
    /// Set this for a printer language such as ZPL, EPL, CPCL or ESC/POS. A raw TCP channel
    /// and the Windows spooler with the <c>RAW</c> data type send the bytes through unchanged.
    /// IPP, IPPS and CUPS do not give this promise. A CUPS raw queue does send the bytes
    /// through, and the library submits a printer language as <c>application/vnd.cups-raw</c>
    /// so that it can, but CUPS reports nothing that tells a raw queue from a queue with a
    /// driver, which still converts the job. The promise is therefore refused for CUPS.
    /// A caller who knows the queue is raw should print without this property: the format
    /// the library sends is correct either way.
    /// The printer manager reads this property and chooses a channel that keeps the promise, or
    /// throws <see cref="NotSupportedException"/> when the printer has no such channel. The
    /// printer types do not read it: a caller that opens a printer directly has already chosen
    /// the channel.
    /// </remarks>
    public bool RequirePassthrough { get; set; }
}
