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
    /// Gets the physical location of the printer, when known.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the driver name, when known.
    /// </summary>
    public string? DriverName { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the default printer.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets a value indicating whether the printer is shared.
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    /// Gets the device UUID the printer reported, without any <c>urn:uuid:</c> prefix,
    /// or <c>null</c> when it reported none.
    /// </summary>
    public string? Uuid { get; init; }

    /// <summary>
    /// Gets the serial number the printer reported, or <c>null</c> when it reported none.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets the manufacturer, when known.
    /// </summary>
    public string? Manufacturer { get; init; }

    /// <summary>
    /// Gets the model, when known.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the printer command languages the device accepts, when reported.
    /// </summary>
    public IReadOnlyList<string> CommandSets { get; init; } = [];
}
