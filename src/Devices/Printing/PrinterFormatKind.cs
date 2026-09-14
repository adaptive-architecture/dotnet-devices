namespace AdaptArch.Devices.Printing;

/// <summary>
/// What a printer does with the bytes of a content type.
/// </summary>
public enum PrinterFormatKind
{
    /// <summary>
    /// Bytes no channel interprets. They travel unchanged, and no print server may
    /// rewrite them into another format. This is the kind of an unregistered content type.
    /// </summary>
    Opaque,

    /// <summary>
    /// Commands the printer firmware reads, such as ZPL. A channel that converts the job
    /// prints the command source instead of the label, so the routing keeps these bytes
    /// on a channel that sends them unchanged.
    /// </summary>
    RawLanguage,

    /// <summary>
    /// A raster the printer driver draws onto the page, such as PNG.
    /// </summary>
    Image,

    /// <summary>
    /// A page description that a converter turns into images before it prints, such as PDF.
    /// </summary>
    Document,
}
