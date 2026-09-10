namespace AdaptArch.Devices.Printing;

/// <summary>
/// How the printer fits a document onto the media, from PWG 5100.16
/// <c>print-scaling</c>.
/// </summary>
/// <remarks>
/// IPP printers and the CUPS spooler driver carry every value. The Windows spooler
/// device mode holds a scale percentage and no fit mode, so it applies
/// <see cref="None"/> as 100 per cent and reports every other value in
/// <see cref="PrintJobInfo.DroppedOptions"/>.
/// </remarks>
public enum PrintScaling
{
    /// <summary>
    /// The printer chooses between <see cref="Fill"/> and <see cref="Fit"/>, from the
    /// document size and the media size.
    /// </summary>
    Auto,

    /// <summary>
    /// The printer scales the document up to the media only when it is smaller, and
    /// otherwise acts as <see cref="Fit"/>.
    /// </summary>
    AutoFit,

    /// <summary>
    /// The document fills the whole media. Any part outside the media is cut off.
    /// </summary>
    Fill,

    /// <summary>
    /// The whole document fits inside the media, and the aspect ratio is kept.
    /// </summary>
    Fit,

    /// <summary>
    /// The document is printed at its own size, and is not scaled.
    /// </summary>
    None,
}
