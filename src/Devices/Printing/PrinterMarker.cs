namespace AdaptArch.Devices.Printing;

/// <summary>
/// Supply marker of a printer, such as an ink cartridge or toner.
/// </summary>
public sealed class PrinterMarker
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterMarker"/> class.
    /// </summary>
    /// <param name="name">The marker name, for example "Black ink".</param>
    public PrinterMarker(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the marker name, for example "Black ink".
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the marker color as reported by the printer, for example "#000000".
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Gets or sets the remaining supply level in percent (0-100), or <c>null</c> when unknown.
    /// </summary>
    public int? LevelPercent { get; set; }
}
