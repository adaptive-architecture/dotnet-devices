namespace AdaptArch.Devices.Printing;

/// <summary>
/// What a printer reports about itself when asked. Every property is <c>null</c> when the
/// printer did not report it, which is not the same as an empty value.
/// </summary>
/// <remarks>
/// This is pure data. The printer reports it; <see cref="IPrinterManager"/> decides what
/// it means for identity and for grouping, so the transports stay free of that policy.
/// </remarks>
public sealed class PrinterIdentity
{
    /// <summary>
    /// Gets the device UUID, without any <c>urn:uuid:</c> prefix, when reported.
    /// </summary>
    public string? Uuid { get; init; }

    /// <summary>
    /// Gets the serial number of the device, when reported.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets the manufacturer, when reported.
    /// </summary>
    public string? Manufacturer { get; init; }

    /// <summary>
    /// Gets the model, when reported.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the combined make and model string, when reported.
    /// </summary>
    public string? MakeAndModel { get; init; }

    /// <summary>
    /// Gets the display name, when reported.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the physical location, when reported.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the printer command languages the device accepts, when reported.
    /// </summary>
    public IReadOnlyList<string> CommandSets { get; init; } = [];

    /// <summary>
    /// Gets the device URI a print queue points at, when the spooler reported one. It is
    /// the only link between a queue and the device behind it.
    /// </summary>
    public string? DeviceUri { get; init; }

    /// <summary>
    /// Gets a value indicating whether the operating system reported this as its default
    /// printer.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets a value indicating whether the operating system reported this printer as
    /// shared.
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    /// Gets the devices this channel was reported to belong to.
    /// </summary>
    /// <remarks>
    /// A spooler fills this from the device URI or the port name of the queue, because
    /// only the driver knows the shape of what it read.
    /// </remarks>
    public IReadOnlyList<PrinterDeviceKey> Aliases { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether the printer reported anything at all.
    /// </summary>
    public bool IsEmpty =>
        Uuid is null && SerialNumber is null && Manufacturer is null && Model is null &&
        MakeAndModel is null && Name is null && Location is null && DeviceUri is null &&
        CommandSets.Count == 0 && Aliases.Count == 0 && !IsDefault && !IsShared;
}
