namespace AdaptArch.Devices.Printing;

/// <summary>
/// How the printer fits a document onto the media, from PWG 5100.16
/// <c>print-scaling</c>.
/// </summary>
/// <remarks>
/// IPP printers and the CUPS spooler driver carry every value. On Windows, printer
/// languages apply <see cref="None"/> as a 100 per cent device mode scale and report
/// every other value in <see cref="PrintJobInfo.DroppedOptions"/>, while PNG and JPEG
/// images are laid out with GDI and honour every value.
/// <para>
/// A CUPS queue on macOS renders a PDF with Quartz, which ignores this value and fits
/// every page as <see cref="Auto"/> does: <see cref="Fit"/> does not enlarge a page that
/// already fits, <see cref="Fill"/> is not applied, and <see cref="None"/> shrinks a page
/// larger than the media instead of cutting it off.
/// </para>
/// </remarks>
public enum PrintScaling
{
    /// <summary>
    /// A document that already fits keeps its own size, and a larger one is scaled to
    /// the media: <see cref="Fit"/> on media with margins, <see cref="Fill"/> on
    /// borderless media. This is the value a printer usually defaults to, and it is
    /// <see cref="AutoFit"/> apart from the borderless case.
    /// </summary>
    Auto,

    /// <summary>
    /// The printer chooses between <see cref="None"/> and <see cref="Fit"/>: a document
    /// that already fits keeps its own size, and a larger one is scaled down.
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
