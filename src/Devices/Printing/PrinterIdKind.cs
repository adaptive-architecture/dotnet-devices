namespace AdaptArch.Devices.Printing;

/// <summary>
/// Identifies the addressing scheme of a <see cref="PrinterId"/> value.
/// </summary>
public enum PrinterIdKind
{
    /// <summary>
    /// Printer installed in the operating system print spooler.
    /// </summary>
    Spooler,

    /// <summary>
    /// Printer addressed directly over the network.
    /// </summary>
    Network,

    /// <summary>
    /// Printer connected over USB.
    /// </summary>
    Usb,
}
