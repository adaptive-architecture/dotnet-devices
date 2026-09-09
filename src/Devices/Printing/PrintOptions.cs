namespace AdaptArch.Devices.Printing;

/// <summary>
/// Per-job printing options. Every property is optional; unset properties
/// fall back to the printer or driver default, which keeps the options
/// portable across spoolers with different capabilities.
/// </summary>
/// <remarks>
/// On the Windows print spooler, only <see cref="JobName"/> reaches the device today.
/// <see cref="Copies"/>, <see cref="Duplex"/>, <see cref="ColorMode"/>,
/// <see cref="Orientation"/>, <see cref="MediaSource"/>, <see cref="MediaSize"/> and
/// <see cref="ResolutionDpi"/> are read and validated, but the Windows driver does not
/// map them into a device mode, so the job goes to the device unchanged. IPP printers
/// and the CUPS spooler driver on Linux and macOS honour every property.
/// </remarks>
public sealed class PrintOptions
{
    /// <summary>
    /// Gets or sets the number of copies. Must be positive when set.
    /// </summary>
    public int? Copies { get; set; }

    /// <summary>
    /// Gets or sets the duplex mode.
    /// </summary>
    public DuplexMode? Duplex { get; set; }

    /// <summary>
    /// Gets or sets the color mode.
    /// </summary>
    public PrintColorMode? ColorMode { get; set; }

    /// <summary>
    /// Gets or sets the page orientation.
    /// </summary>
    public PrintOrientation? Orientation { get; set; }

    /// <summary>
    /// Gets or sets the media source (tray) name.
    /// </summary>
    public string? MediaSource { get; set; }

    /// <summary>
    /// Gets or sets the media (paper or label) size name.
    /// </summary>
    public string? MediaSize { get; set; }

    /// <summary>
    /// Gets or sets the print resolution in dots per inch.
    /// </summary>
    public int? ResolutionDpi { get; set; }

    /// <summary>
    /// Gets or sets the human-readable job name shown in print queues.
    /// </summary>
    public string? JobName { get; set; }

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
    /// IPP, IPPS and CUPS can filter or rasterise a document, so they do not give this promise.
    /// The printer manager reads this property and chooses a channel that keeps the promise, or
    /// throws <see cref="NotSupportedException"/> when the printer has no such channel. The
    /// printer types do not read it: a caller that opens a printer directly has already chosen
    /// the channel.
    /// </remarks>
    public bool RequirePassthrough { get; set; }
}
