namespace AdaptArch.Devices.Printing;

/// <summary>
/// Descriptive information about a printer.
/// </summary>
public sealed class PrinterInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterInfo"/> class.
    /// </summary>
    /// <param name="id">The printer identifier.</param>
    /// <param name="name">The display name of the printer.</param>
    public PrinterInfo(PrinterId id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }

    /// <summary>
    /// Gets the printer identifier.
    /// </summary>
    public PrinterId Id { get; }

    /// <summary>
    /// Gets the display name of the printer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the physical location of the printer, when known.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the driver name, when known.
    /// </summary>
    public string? DriverName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the default printer.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the printer is shared.
    /// </summary>
    public bool IsShared { get; set; }
}
