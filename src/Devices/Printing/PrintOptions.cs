namespace AdaptArch.Devices.Printing;

/// <summary>
/// Per-job printing options. Every property is optional; unset properties
/// fall back to the printer or driver default, which keeps the options
/// portable across spoolers with different capabilities.
/// </summary>
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
}
