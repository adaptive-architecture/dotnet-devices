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
    }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId PrinterId { get; }

    /// <summary>
    /// Gets or sets the supported print resolutions in dots per inch.
    /// </summary>
    public IReadOnlyList<int> SupportedResolutionsDpi { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether duplex printing is supported.
    /// </summary>
    public bool SupportsDuplex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether color printing is supported.
    /// </summary>
    public bool SupportsColor { get; set; }

    /// <summary>
    /// Gets or sets the supported media (paper or label) size names.
    /// </summary>
    public IReadOnlyList<string> MediaSizes { get; set; }

    /// <summary>
    /// Gets or sets the default media size name, when known.
    /// </summary>
    public string? DefaultMediaSize { get; set; }
}
