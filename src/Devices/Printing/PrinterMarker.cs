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

    /// <summary>
    /// Gets or sets the raw supply level as reported by the printer, or <c>null</c> when
    /// the printer did not report one. The Printer MIB (RFC 3805) uses negative values for
    /// a level that is not a quantity: <c>-1</c> means "other", <c>-2</c> means "some
    /// amount remains, unknown", and <c>-3</c> means "some amount remains". When
    /// <see cref="LevelPercent"/> is <c>null</c> for one of those reasons, this property
    /// still holds the raw negative value, so a caller can tell them apart.
    /// </summary>
    public long? LevelRaw { get; set; }

    /// <summary>
    /// Gets or sets the maximum supply capacity as reported by the printer, in the same
    /// unit as <see cref="LevelRaw"/>, or <c>null</c> when the printer did not report one.
    /// </summary>
    public long? MaxCapacity { get; set; }
}
