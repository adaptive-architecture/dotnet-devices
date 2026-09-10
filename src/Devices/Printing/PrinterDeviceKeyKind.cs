namespace AdaptArch.Devices.Printing;

/// <summary>
/// Says what kind of value a <see cref="PrinterDeviceKey"/> holds, so that two keys with
/// the same text but a different meaning never compare equal.
/// </summary>
public enum PrinterDeviceKeyKind
{
    /// <summary>
    /// An identity the device reported about itself: a UUID or a serial number. This is
    /// the only kind that survives a change of address.
    /// </summary>
    DeviceIdentity,

    /// <summary>
    /// A host name or an IP address. It identifies a device only as far as the network
    /// does: behind a print server or a network address translation, two devices can
    /// share one host.
    /// </summary>
    Host,

    /// <summary>
    /// The name of a print queue of the operating system spooler.
    /// </summary>
    Queue,
}
