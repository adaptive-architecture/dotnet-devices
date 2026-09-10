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
    /// Gets the supported media (paper or label) size names.
    /// </summary>
    public IReadOnlyList<string> MediaSizes { get; init; }

    /// <summary>
    /// Gets the default media size name, when known.
    /// </summary>
    public string? DefaultMediaSize { get; init; }
}
