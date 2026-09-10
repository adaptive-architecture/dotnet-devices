namespace AdaptArch.Devices.Printing;

/// <summary>
/// Names a protocol that answered a question about a device. These are read-only
/// channels: a job cannot be sent over them, so they are not <see cref="PrinterScheme"/>
/// values and never appear among the channels of a <see cref="PrinterDevice"/>.
/// </summary>
public enum PrinterStatusSource
{
    /// <summary>
    /// The Internet Printing Protocol, which reports the state, the make and model, the
    /// supply markers and, on many printers, the UUID and the device identification.
    /// </summary>
    Ipp,

    /// <summary>
    /// Simple Network Management Protocol version 2c, which reports the serial number and
    /// the lifetime page count that IPP does not carry.
    /// </summary>
    Snmp,

    /// <summary>
    /// The print spooler of the operating system.
    /// </summary>
    Spooler,
}
