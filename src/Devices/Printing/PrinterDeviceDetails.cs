namespace AdaptArch.Devices.Printing;

/// <summary>
/// Everything known about one physical device, gathered from every channel that reaches
/// it. A property is <c>null</c> or empty when no channel reported it.
/// </summary>
/// <remarks>
/// Channels disagree, so each field has one winner and the rest stay visible on
/// <see cref="PrinterDevice.Channels"/>. Nothing here is concatenated or averaged.
/// <para>
/// A fact about the hardware — the UUID, the serial number, the manufacturer and the
/// model — is taken from the channel that speaks to the device itself, in the order
/// IPP over TLS, IPP, raw, spooler.
/// </para>
/// <para>
/// A fact meant for a person — the name and the location — is taken from the spooler
/// first, because that is the text the operating system already shows the user, and only
/// then from the device.
/// </para>
/// </remarks>
public sealed class PrinterDeviceDetails
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrinterDeviceDetails"/> class.
    /// </summary>
    /// <param name="name">The display name of the device.</param>
    public PrinterDeviceDetails(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the display name of the device.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the device UUID, when a channel reported one.
    /// </summary>
    public string? Uuid { get; init; }

    /// <summary>
    /// Gets the serial number, when a channel reported one.
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets the manufacturer, when a channel reported one.
    /// </summary>
    public string? Manufacturer { get; init; }

    /// <summary>
    /// Gets the model, when a channel reported one.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the physical location, when a channel reported one.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the driver name, when a channel reported one.
    /// </summary>
    public string? DriverName { get; init; }

    /// <summary>
    /// Gets the printer command languages the device accepts, when a channel reported them.
    /// </summary>
    public IReadOnlyList<string> CommandSets { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this is the default printer of the operating system.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Gets a value indicating whether the printer is shared.
    /// </summary>
    public bool IsShared { get; init; }

    /// <summary>
    /// Gets the discoveries that reported a channel of this device.
    /// </summary>
    public IReadOnlyList<DiscoverySource> ContributedBy { get; init; } = [];

    /// <summary>
    /// Gets the read-only protocols that answered about this device. A job cannot be sent
    /// over any of them; they only report what the device says about itself.
    /// </summary>
    public IReadOnlyList<PrinterStatusSource> StatusSources { get; init; } = [];
}
