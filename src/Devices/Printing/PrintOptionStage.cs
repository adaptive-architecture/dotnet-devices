namespace AdaptArch.Devices.Printing;

/// <summary>
/// The step of the print path at which a <see cref="DroppedOption"/> was dropped.
/// </summary>
public enum PrintOptionStage
{
    /// <summary>
    /// The printer does not list the value among its capabilities, and
    /// <see cref="UnsupportedOptionBehavior.Drop"/> removed it. Pick a value the printer
    /// reports in <see cref="PrinterConfiguration"/>.
    /// </summary>
    PrinterCapabilities,

    /// <summary>
    /// The Windows spooler carries the option in a device mode, which has no field for it or
    /// whose driver did not apply it. Set it on the queue, or use an IPP channel.
    /// </summary>
    DeviceMode,

    /// <summary>
    /// The option is applied when the library renders the document, and the job was not
    /// rendered: the format is not converted on this path, no converter was registered, or
    /// the printer reads nothing the converter writes.
    /// </summary>
    Conversion,

    /// <summary>
    /// The channel has nowhere to carry the option: a raw socket sends the bytes with no job
    /// template, and CUPS receives the document as it is.
    /// </summary>
    Channel,
}
